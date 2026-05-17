using System.Text.Json;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;

namespace StockAnalysis.Web.Services;

public class DailyWatchPoolService
{
    private readonly StockAnalyzer _analyzer;
    private readonly DataSourceFallbackService _ds;
    private readonly MainstreamSectorScanner _sectorScanner = new();
    private readonly HotSectorRotationEngine _rotationEngine = new();
    private readonly string _logPath;
    private readonly string _cachePath;

    public DailyWatchPoolService(StockAnalyzer analyzer, DataSourceFallbackService ds, IWebHostEnvironment env)
    {
        _analyzer = analyzer;
        _ds = ds;
        var dataDir = Path.Combine(env.ContentRootPath, "..", "Data");
        _logPath = Path.Combine(dataDir, "watch_pool_log.json");
        _cachePath = Path.Combine(dataDir, "candidate_cache.json");
        Directory.CreateDirectory(dataDir);
    }

    public async Task<WatchPoolResult> GenerateAsync(
        List<StockBar>? marketBars,
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        // ── 步骤1：一次性获取全市场快照，本地内存过滤 ────────────
        var snapshot = await _ds.GetMarketSnapshotAsync();
        var snapshotMap = snapshot.ToDictionary(s => s.Code);

        // ── 步骤2：用快照全量数据评估市场整体热度（不依赖板块成分股接口）────
        var marketResult = EvaluateSectorFromSnapshot("全市场", snapshot
            .Where(s => s.Price > 0 && !s.Name.Contains("ST")).Take(200).ToList());
        var top10 = new List<(string, MainstreamSectorResult, List<(string, string)>)>
        {
            ("全市场", marketResult, [])
        };

        var previousScores = await LoadPreviousSectorScoresAsync();
        var currentScores = top10.ToDictionary(x => x.Item1, x => x.Item2.SectorHeatScore);
        var rotations = _rotationEngine.Analyze(previousScores, currentScores);

        // ── 步骤3：直接从快照过滤候选股 ─────────────────────────
        var cache = await LoadCacheAsync();
        var prioritySet = cache
            .Where(c => c.ConsecutiveAppearDays >= 2 || c.IsPreviousValuePool)
            .ToDictionary(c => c.StockCode, c => c);

        var candidateSet = snapshot
            .Where(s => s.Price > 0 && s.Price < 30m && s.Amount >= 500_0000m
                && !s.Name.Contains("ST") && !s.Name.Contains("退")
                && s.Code.Length == 6
                && !s.Code.StartsWith("688") && !s.Code.StartsWith("300")
                && !s.Code.StartsWith("301") && !s.Code.StartsWith("8") && !s.Code.StartsWith("4")
                && (s.TotalMv == 0 || s.TotalMv / 1_0000_0000m < 300m))
            .ToDictionary(s => s.Code, s => s.Name);

        foreach (var item in prioritySet.Values)
            candidateSet.TryAdd(item.StockCode, item.StockName);

        // ── 步骤4：排序取候选股（已在步骤3过滤完毕）────────────
        var candidates = candidateSet
            .Select(kv => (Code: kv.Key, Name: kv.Value,
                Priority: prioritySet.TryGetValue(kv.Key, out var c) ? c.ScanPriorityScore : 0))
            .OrderByDescending(x => x.Priority)
            .Take(1000)
            .ToList();

        // ── 步骤5：只对候选股请求历史K线并分析 ──────────────────
        int scanned = 0, failed = 0, filtered = 0, matched = 0, done = 0;
        int totalCount = candidates.Count;
        var startTime = DateTime.UtcNow;
        string? currentStock = null;
        var semaphore = new SemaphoreSlim(8);
        var sectorResultMap = top10.ToDictionary(x => x.Item1, x => x.Item2);

        var tasks = candidates.Select(async s =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref currentStock, s.Name);
                var bars = await _ds.GetHistoryBarsAsync(s.Code, s.Name);
                Interlocked.Increment(ref done);
                progress?.Report((done, candidates.Count, s.Name));

                if (bars == null || bars.Count < 60) { Interlocked.Increment(ref filtered); return null; }

                var latestClose = bars[^1].Close;

                Interlocked.Increment(ref scanned);

                var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
                var signal = signals.FirstOrDefault();
                if (signal == null) return null;

                var plat = signal.MainUpPlatform;
                if (plat == null || !plat.IsMainUpPlatform) return null;
                if (plat.SecondWaveProbability < 60 || plat.LockPositionStrength < 60) return null;
                if (signal.ChipControl?.ChipLockScore < 60) return null;
                if (plat.PlatformDays < 3 || plat.PlatformDays > 10) return null;
                if (plat.PlatformRangePercent > 12m) return null;
                if (signal.RiskScore > 50) return null;
                if (signal.SupportPrice.HasValue && latestClose < signal.SupportPrice.Value) return null;

                var snapItem = snapshotMap.GetValueOrDefault(s.Code);
                var price = snapItem?.Price > 0 ? snapItem.Price
                    : (await _ds.GetRealtimeQuoteAsync(s.Code))?.Price ?? latestClose;

                // 市值已在快照过滤阶段完成（TotalMv == 0 或 < 300亿），此处不再重复查询

                var chipLock = signal.ChipControl?.ChipLockScore ?? 0m;
                var sectorScore = signal.SectorEmotion?.SectorStrengthScore ?? 50m;
                var emotionCycle = signal.SectorEmotion?.Cycle.ToString() ?? "";
                var resonance = signal.SectorResonance;
                var resonanceScore = resonance?.ResonanceScore ?? 50m;

                if (resonanceScore < 40 && resonance?.IsIndependentPump == true) return null;

                Interlocked.Increment(ref matched);
                var sectorName = "";
                var mainstreamResult = sectorResultMap.GetValueOrDefault("全市场");

                var sectorBonus = mainstreamResult?.IsMainstream == true ? 5m
                    : mainstreamResult?.IsHotSector == true ? 2m
                    : mainstreamResult?.IsDeclining == true ? -5m : 0m;

                var score = plat.SecondWaveProbability * 0.25m
                          + plat.LockPositionStrength * 0.20m
                          + chipLock * 0.20m
                          + sectorScore * 0.15m
                          + signal.IntradayStrengthScore * 0.10m
                          + (100 - signal.RiskScore) * 0.10m
                          + sectorBonus;

                var tier = score >= 80 && plat.SecondWaveProbability >= 75 && chipLock >= 70 && signal.RiskScore <= 40
                    ? "激进"
                    : score >= 70 && plat.SecondWaveProbability >= 65 && signal.RiskScore <= 50 ? "重点"
                    : score >= 60 ? "普通" : null;
                if (tier == null) return null;

                if (mainstreamResult?.IsDeclining == true && tier == "激进") tier = "重点";
                if (signal.SectorEmotion?.Cycle == SectorEmotionCycle.Decline && tier == "激进") tier = "重点";
                if (mainstreamResult?.IsHotSector != true && tier == "激进") tier = "重点";
                if (resonanceScore < 50 && tier == "激进") tier = "重点";
                if (resonanceScore < 40 && tier == "重点") tier = "普通";

                bool isValuePool = plat.IsMainUpPlatform
                    && plat.SecondWaveProbability >= 70 && chipLock >= 70
                    && signal.SectorEmotion?.Cycle != SectorEmotionCycle.Decline
                    && resonanceScore >= 70 && resonance?.IsIndependentPump != true
                    && signal.RiskScore <= 45
                    && mainstreamResult?.SectorHeatScore >= 70
                    && mainstreamResult?.IsDeclining != true;

                var rotation = rotations.FirstOrDefault(r => r.SectorName == sectorName);

                return new WatchPoolItem
                {
                    Code = s.Code, Name = s.Name, Price = price,
                    MarketCap = snapItem?.TotalMv > 0 ? snapItem.TotalMv / 1_0000_0000m : null,
                    Sector = sectorName,
                    WatchPoolScore = Math.Round(score, 1),
                    SecondWaveProbability = plat.SecondWaveProbability,
                    LockPositionStrength = plat.LockPositionStrength,
                    ChipLockScore = chipLock, SectorEmotion = emotionCycle,
                    RiskScore = signal.RiskScore, Decision = signal.Decision, Tier = tier,
                    ResonanceScore = resonanceScore,
                    SectorRisingCount = resonance?.SectorRisingCount ?? 0,
                    SectorLimitUpCount = resonance?.SectorLimitUpCount ?? 0,
                    IsIndependentPump = resonance?.IsIndependentPump ?? false,
                    IsFakeBreakoutRisk = resonance?.IsFakeBreakoutRisk ?? false,
                    IsValuePool = isValuePool,
                    SectorHeatScore = mainstreamResult?.SectorHeatScore ?? 0,
                    IsMainstreamSector = mainstreamResult?.IsMainstream ?? false,
                    IsSectorDeclining = mainstreamResult?.IsDeclining ?? false,
                    SectorRotationNote = rotation?.Summary ?? "",
                    Reason = BuildReason(signal, plat, emotionCycle, resonance, sectorName, mainstreamResult),
                    RiskWarning = BuildWarning(signal, emotionCycle, resonance, mainstreamResult)
                };
            }
            catch { Interlocked.Increment(ref failed); return null; }
            finally { semaphore.Release(); }
        });

        var all = await Task.WhenAll(tasks);
        var items = all.Where(x => x != null).Cast<WatchPoolItem>()
            .OrderByDescending(x => x.WatchPoolScore).ToList();

        var aggressive = items.Where(x => x.Tier == "激进").Take(5).ToList();
        var key = items.Where(x => x.Tier == "重点").Take(10).ToList();
        var normal = items.Where(x => x.Tier == "普通").Take(15).ToList();

        var final = aggressive.Concat(key).Concat(normal)
            .OrderByDescending(x => x.WatchPoolScore).Take(30).ToList();
        for (int i = 0; i < final.Count; i++) final[i].Rank = i + 1;

        await UpdateCacheAsync(final, cache);

        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        var completedRate = totalCount > 0 ? (double)(scanned + failed + filtered) / totalCount : 0;
        var estimatedRemaining = completedRate > 0.01 ? elapsed / completedRate - elapsed : 0;

        var result = new WatchPoolResult
        {
            Items = final,
            TotalCount = totalCount,
            ScannedCount = scanned,
            FailedCount = failed,
            FilteredCount = filtered,
            MatchedCount = matched,
            CurrentStockCode = candidates.Count > 0 ? candidates[0].Code : null,
            CurrentStockName = currentStock,
            ElapsedSeconds = Math.Round(elapsed, 1),
            EstimatedRemainingSeconds = Math.Round(estimatedRemaining, 1),
            Top10Sectors = top10.Select(x => x.Item2).ToList(),
            SectorRotations = rotations,
            DataSourceName = _ds.Status.CurrentSource,
            DataSourceFailCount = _ds.Status.FailCount,
            UsingCache = _ds.Status.UsingCache,
            SkippedCount = _ds.Status.SkippedCount,
            DataSourceWarnings = _ds.Status.Warnings
        };
        await SaveLogAsync(result);
        return result;
    }

    /// <summary>用快照数据直接评估板块热度（不拉K线，速度快）</summary>
    private static MainstreamSectorResult EvaluateSectorFromSnapshot(string sector, List<MarketSnapshotItem> snaps)
    {
        int total = snaps.Count, rising = 0, falling = 0, limitUp = 0, strong = 0;
        decimal sumGain = 0, maxGain = 0;
        foreach (var s in snaps)
        {
            var gain = s.ChangePct / 100m;
            sumGain += gain;
            if (gain > 0) rising++;
            else if (gain < 0) falling++;
            if (gain >= 0.095m) { limitUp++; strong++; }
            else if (gain >= 0.03m) strong++;
            if (gain > maxGain) maxGain = gain;
        }
        decimal risingRatio = (decimal)rising / total;
        decimal avgGain = sumGain / total;
        decimal leaderStrength = Math.Clamp(maxGain / 0.10m * 100, 0, 100);
        decimal trendStrength = Math.Clamp(risingRatio * 60 + (decimal)limitUp / total * 40 * 10, 0, 100);
        decimal capitalFlow = Math.Clamp((decimal)limitUp / total * 50 + (decimal)strong / total * 50, 0, 100);
        decimal continuity = Math.Clamp(50 + avgGain * 500, 0, 100);
        decimal heatScore = Math.Clamp(
            risingRatio * 100 * 0.20m
            + Math.Min((decimal)limitUp / total * 100 * 3, 100) * 0.15m
            + leaderStrength * 0.20m + capitalFlow * 0.15m
            + (decimal)strong / total * 100 * 0.15m + trendStrength * 0.15m, 0, 100);

        bool isMainstream = heatScore >= 80, isHot = heatScore >= 65;
        bool isDeclining = risingRatio < 0.3m || (limitUp == 0 && avgGain < -0.005m);
        var summary = isMainstream ? $"主线板块，上涨{rising}/{total}只，涨停{limitUp}只"
            : isDeclining ? $"板块退潮，热度{heatScore:F0}分" : $"板块热度{heatScore:F0}分";

        return new MainstreamSectorResult(sector, Math.Round(heatScore, 1), Math.Round(capitalFlow, 1),
            Math.Round(trendStrength, 1), Math.Round(continuity, 1), Math.Round(leaderStrength, 1),
            rising, falling, limitUp, strong, isMainstream, isHot, isDeclining, summary);
    }

    private static string BuildReason(StockSignal s, MainUpPlatformResult plat, string emotion,
        SectorResonanceResult? resonance, string sectorName, MainstreamSectorResult? mainstream)
    {
        var label = emotion switch {
            "Recovery" => "修复", "Consensus" => "一致", "Climax" => "高潮",
            "Divergence" => "分歧", "Decline" => "退潮", _ => "中性"
        };
        var sectorNote = mainstream?.IsMainstream == true
            ? $"属于当前市场主线板块【{sectorName}】，板块热度{mainstream.SectorHeatScore:F0}分，"
            : !string.IsNullOrEmpty(sectorName) ? $"所属板块【{sectorName}】，" : "";
        var resonanceNote = resonance?.IsSectorResonance == true
            ? $"，板块共振（{resonance.ResonanceScore:F0}分）" : "";
        return $"{sectorNote}该股近期已形成主升浪，平台横盘{plat.PlatformDays}日缩量整理，" +
               $"筹码锁定分{s.ChipControl?.ChipLockScore:F0}，二波概率{plat.SecondWaveProbability:F0}%，" +
               $"板块处于{label}阶段{resonanceNote}，适合加入明日观察池。";
    }

    private static string BuildWarning(StockSignal s, string emotion,
        SectorResonanceResult? resonance, MainstreamSectorResult? mainstream)
    {
        if (mainstream?.IsDeclining == true) return "该股所属板块正在退潮，资金撤离，谨慎参与。";
        if (resonance?.IsIndependentPump == true) return "该股当前上涨缺乏板块支撑，疑似主力自救或诱多，谨慎追高。";
        if (resonance?.IsFakeBreakoutRisk == true) return "该股存在诱多/尾盘偷拉风险，建议观察，不适合追高。";
        if (s.RiskScore > 40 || emotion is "Decline" or "Divergence")
            return "该股虽有主升平台结构，但板块分歧较大或风险分偏高，只适合观察，不适合追高。";
        return "";
    }

    private async Task UpdateCacheAsync(List<WatchPoolItem> newItems, List<CandidateStockItem> existingCache)
    {
        var cacheMap = existingCache.ToDictionary(c => c.StockCode);
        foreach (var item in newItems)
        {
            if (cacheMap.TryGetValue(item.Code, out var existing))
            {
                existing.ConsecutiveAppearDays++;
                existing.LastScore = item.WatchPoolScore;
                existing.LastDecision = item.Decision.ToString();
                existing.LastAnalyzeTime = DateTime.Today;
                existing.IsPreviousWatchPool = true;
                existing.IsPreviousValuePool = item.IsValuePool;
                existing.ResonanceScore = item.ResonanceScore;
                existing.ChipLockScore = item.ChipLockScore;
                existing.MainUpPlatformScore = item.SecondWaveProbability;
                existing.Sector = item.Sector;
            }
            else
            {
                cacheMap[item.Code] = new CandidateStockItem
                {
                    StockCode = item.Code, StockName = item.Name, Sector = item.Sector,
                    LastScore = item.WatchPoolScore, LastDecision = item.Decision.ToString(),
                    LastAnalyzeTime = DateTime.Today, ConsecutiveAppearDays = 1,
                    IsPreviousWatchPool = true, IsPreviousValuePool = item.IsValuePool,
                    ResonanceScore = item.ResonanceScore, ChipLockScore = item.ChipLockScore,
                    MainUpPlatformScore = item.SecondWaveProbability
                };
            }
        }
        var cutoff = DateTime.Today.AddDays(-5);
        var updated = cacheMap.Values.Where(c => c.LastAnalyzeTime >= cutoff).ToList();
        await File.WriteAllTextAsync(_cachePath,
            JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<List<CandidateStockItem>> LoadCacheAsync()
    {
        if (!File.Exists(_cachePath)) return [];
        try { return JsonSerializer.Deserialize<List<CandidateStockItem>>(await File.ReadAllTextAsync(_cachePath)) ?? []; }
        catch { return []; }
    }

    private async Task<Dictionary<string, decimal>> LoadPreviousSectorScoresAsync()
    {
        var history = await LoadHistoryAsync();
        var latest = history.FirstOrDefault();
        if (latest?.Top10Sectors == null) return [];
        return latest.Top10Sectors.ToDictionary(s => s.SectorName, s => s.SectorHeatScore);
    }

    public async Task<List<WatchPoolResult>> LoadHistoryAsync()
    {
        if (!File.Exists(_logPath)) return [];
        try { return JsonSerializer.Deserialize<List<WatchPoolResult>>(await File.ReadAllTextAsync(_logPath)) ?? []; }
        catch { return []; }
    }

    private async Task SaveLogAsync(WatchPoolResult result)
    {
        var history = await LoadHistoryAsync();
        history.RemoveAll(h => h.Date.Date == result.Date.Date);
        history.Insert(0, result);
        if (history.Count > 30) history = history.Take(30).ToList();
        await File.WriteAllTextAsync(_logPath,
            JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
    }
}
