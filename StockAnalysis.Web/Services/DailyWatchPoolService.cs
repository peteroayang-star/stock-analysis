using System.Collections.Concurrent;
using System.Text.Json;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;

namespace StockAnalysis.Web.Services;

public class DailyWatchPoolService
{
    /// <summary>扫描进度（供前端轮询）</summary>
    private static readonly ConcurrentDictionary<string, ScanProgress> ScanProgresses = new();

    public static ScanProgress? GetScanProgress(string scanId)
        => ScanProgresses.TryGetValue(scanId, out var p) ? p : null;

    public static void InitScanProgress(string scanId)
    {
        ScanProgresses[scanId] = new ScanProgress(
            0, 0, 0, 0, 0, "准备中...", "", "", 0, "Running", null, DateTime.UtcNow);
    }

    public static void UpdateScanProgressFail(string scanId, string message)
    {
        if (ScanProgresses.TryGetValue(scanId, out var existing))
            ScanProgresses[scanId] = existing with { Status = "Failed", Message = message, UpdatedAt = DateTime.UtcNow };
    }

    private static void UpdateScanProgress(string? scanId, int done, int total, int matched, int filtered, int failed,
        string currentStock, string currentSector, string currentSource, int failedSources, string status)
    {
        if (scanId == null) return;
        ScanProgresses[scanId] = new ScanProgress(
            done, total, matched, filtered, failed,
            currentStock, currentSector, currentSource,
            failedSources, status, null, DateTime.UtcNow);
    }

    private readonly StockAnalyzer _analyzer;
    private readonly DataSourceFallbackService _ds;
    private readonly RiskStockTagEngine _riskTag;
    private readonly MarketContextEngine _marketCtx = new();
    private readonly string _logPath;
    private readonly string _cachePath;
    private readonly string _dataDir;

    public DailyWatchPoolService(StockAnalyzer analyzer, DataSourceFallbackService ds,
        RiskStockTagEngine riskTag, IWebHostEnvironment env)
    {
        _analyzer = analyzer;
        _ds = ds;
        _riskTag = riskTag;
        _dataDir = Path.Combine(env.ContentRootPath, "..", "Data");
        _logPath = Path.Combine(_dataDir, "watch_pool_log.json");
        _cachePath = Path.Combine(_dataDir, "candidate_cache.json");
        Directory.CreateDirectory(_dataDir);
    }

    // ═══════════════════════════════════════════════════════════════
    // 新逻辑：市场 → 主线板块 → 龙头识别 → 个股筛选
    // ═══════════════════════════════════════════════════════════════

    public async Task<WatchPoolResult> GenerateAsync(
        List<StockBar>? marketBars,
        List<string>? targetSectors = null,
        bool fullMarket = false,
        string? scanId = null,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        Console.WriteLine($"[WatchPool] === 扫描开始 {DateTime.Now:HH:mm:ss} ===");

        // ── 步骤1: 获取快照 + 市场上下文 ──────────────────────
        var snapshot = await _ds.GetMarketSnapshotAsync();
        Console.WriteLine($"[WatchPool] 快照: {snapshot.Count:N0} 只");

        if (snapshot.Count == 0)
        {
            var localResult = await ScanLocalCsvFiles(marketBars, fullMarket, targetSectors, scanId, startTime, ct);
            if (localResult != null) return localResult;
            return EmptyResult("全市场快照为空且本地无CSV缓存。请先启动AKShare Flask服务: python akshare_server.py", scanId);
        }

        // 全市场情绪统计
        int totalSnap = snapshot.Count;
        int risingSnap = snapshot.Count(s => s.ChangePct > 0);
        int fallingSnap = snapshot.Count(s => s.ChangePct < 0);
        int limitUpSnap = snapshot.Count(s => s.ChangePct >= 9.5m);
        int limitDownSnap = snapshot.Count(s => s.ChangePct <= -9.5m);
        decimal avgGain = totalSnap > 0 ? snapshot.Average(s => s.ChangePct) : 0;

        // ── 步骤2: 主线板块识别 ──────────────────────────────
        List<MainlineSector> mainlines;

        if (targetSectors != null && targetSectors.Count > 0)
        {
            // 用户指定板块 → 获取成分股计算热度
            var sectorResults = new List<(string, MainstreamSectorResult)>();
            foreach (var sec in targetSectors.Take(10))
            {
                var stocks = await _ds.GetSectorStocksAsync(sec);
                var sectorSnaps = stocks
                    .Select(s => snapshot.FirstOrDefault(x => x.Code == s.Code))
                    .Where(x => x != null).Cast<MarketSnapshotItem>().ToList();
                var heat = EvaluateSectorFromSnapshot(sec, sectorSnaps.Count > 0 ? sectorSnaps : []);
                sectorResults.Add((sec, heat));
            }
            mainlines = BuildMainlines(sectorResults, targetSectors.Count);
            Console.WriteLine($"[WatchPool] 用户指定板块: {mainlines.Count} 个");
        }
        else if (!fullMarket)
        {
            // 默认：获取所有板块，取 TOP5
            var allSectors = await _ds.GetSectorsAsync();
            var sectorResults = new List<(string, MainstreamSectorResult)>();
            int sectorIdx = 0;
            foreach (var sec in allSectors.Take(20))
            {
                var stocks = await _ds.GetSectorStocksAsync(sec);
                var sectorSnaps = stocks
                    .Select(s => snapshot.FirstOrDefault(x => x.Code == s.Code))
                    .Where(x => x != null).Cast<MarketSnapshotItem>().ToList();
                if (sectorSnaps.Count < 5) continue;
                var heat = EvaluateSectorFromSnapshot(sec, sectorSnaps);
                sectorResults.Add((sec, heat));
                sectorIdx++;
                if (sectorIdx >= 10) break;
            }
            mainlines = BuildMainlines(sectorResults, allSectors.Count)
                .OrderByDescending(m => m.HeatScore).Take(5).ToList();
            Console.WriteLine($"[WatchPool] TOP5主线板块: {string.Join(", ", mainlines.Select(m => m.SectorName))}");
        }
        else
        {
            // 全市场模式：从快照直接分析
            var marketHeat = EvaluateSectorFromSnapshot("全市场", snapshot
                .Where(s => s.Price > 0 && !s.Name.Contains("ST")).Take(200).ToList());
            mainlines = BuildMainlines([("全市场", marketHeat)], 1);
            Console.WriteLine("[WatchPool] 全市场模式");
        }

        if (mainlines.Count == 0)
        {
            return EmptyResult("未找到有效主线板块。请检查板块成分股接口是否正常。", scanId);
        }

        // ── 步骤3: 市场情绪周期 ──────────────────────────────
        var marketCtxResult = _marketCtx.Analyze(
            totalSnap, risingSnap, limitUpSnap, avgGain,
            mainlines.Select(m => (m.SectorName, new MainstreamSectorResult(
                m.SectorName, m.HeatScore, 0, 0, m.ContinuityScore, 0,
                m.RisingCount, 0, m.LimitUpCount, 0,
                m.HeatScore >= 80, m.HeatScore >= 65, false, ""
            ))).ToList()
        );

        // ── 步骤4: 板块内个股筛选 ──────────────────────────
        var allCandidates = new List<(string Code, string Name, string Sector, int SectorRank, decimal Amount, decimal ChangePct)>();
        var snapshotMap = snapshot.ToDictionary(s => s.Code);
        int totalCandidates = 0;

        foreach (var mainline in mainlines)
        {
            var stocks = await _ds.GetSectorStocksAsync(mainline.SectorName);
            if (stocks.Count == 0) continue;

            // 按成交额排序取 TOP20
            var sectorStocks = stocks
                .Select(s =>
                {
                    var snap = snapshotMap.GetValueOrDefault(s.Code);
                    return (s.Code, s.Name, Amount: snap?.Amount ?? 0, ChangePct: snap?.ChangePct ?? 0);
                })
                .Where(s => s.Amount > 0 && SecurityTypeFilter.IsCommonAStock(s.Code).IsValid)
                .OrderByDescending(s => s.Amount)
                .Take(20)
                .ToList();

            int rank = 0;
            foreach (var s in sectorStocks)
            {
                rank++;
                allCandidates.Add((s.Code, s.Name, mainline.SectorName, rank, s.Amount, s.ChangePct));
            }
            totalCandidates += sectorStocks.Count;
            Console.WriteLine($"[WatchPool] 板块[{mainline.SectorName}]: {stocks.Count}成分股 → {sectorStocks.Count}候选");
        }

        // 去重（同一只股票可能属于多个板块，保留热度最高的板块）
        var uniqueCandidates = allCandidates
            .GroupBy(c => c.Code)
            .Select(g => g.First())
            .OrderByDescending(c => c.Amount)
            .Take(fullMarket ? 200 : 100)
            .ToList();

        Console.WriteLine($"[WatchPool] 候选股: {uniqueCandidates.Count} 只 (来自{mainlines.Count}个板块)");

        // 初始化进度
        UpdateScanProgress(scanId, 0, uniqueCandidates.Count, 0, 0, 0, "准备中",
            string.Join(",", mainlines.Take(3).Select(m => m.SectorName)), "", 0, "Running");

        // ── 步骤5: 个股分析 + 龙头识别 ────────────────────
        int scanned = 0, failed = 0, filtered = 0, matched = 0, done = 0;
        int totalCount = uniqueCandidates.Count;
        string? currentStock = null;
        var semaphore = new SemaphoreSlim(8);
        int failedSources = 0;

        var tasks = uniqueCandidates.Select(async c =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref currentStock, c.Name);

                var bars = await _ds.GetHistoryBarsAsync(c.Code, c.Name);
                Interlocked.Increment(ref done);

                UpdateScanProgress(scanId, done, totalCount, matched, filtered, failed,
                    c.Name, c.Sector, _ds.Status.CurrentSource, failedSources, "Running");

                if (bars == null || bars.Count < 60) { Interlocked.Increment(ref filtered); return null; }

                Interlocked.Increment(ref scanned);
                var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
                var signal = signals.FirstOrDefault();
                if (signal == null) { return null; }

                // 宽松条件
                var trendOk = signal.Trend == Trend.Up || (signal.MainUpPlatform?.IsMainUpPlatform == true);
                var riskOk = signal.RiskScore <= 65;
                if (!trendOk && !riskOk) { return null; }

                Interlocked.Increment(ref matched);

                var price = c.Amount > 0 ? (snapshotMap.GetValueOrDefault(c.Code)?.Price ?? bars[^1].Close) : bars[^1].Close;
                var plat = signal.MainUpPlatform;
                var chipLock = signal.ChipControl?.ChipLockScore ?? 0m;
                var sectorScore = signal.SectorEmotion?.SectorStrengthScore ?? 50m;

                // 龙头识别
                var mainline = mainlines.FirstOrDefault(m => m.SectorName == c.Sector);
                var leaderPos = MarketContextEngine.IdentifyLeaderRole(
                    signal, c.Amount, c.ChangePct, mainline, c.SectorRank);

                // 综合评分：板块权重优先
                var score = (mainline?.HeatScore ?? 50m) * 0.20m
                          + (plat?.SecondWaveProbability ?? 50m) * 0.15m
                          + chipLock * 0.15m
                          + sectorScore * 0.10m
                          + signal.IntradayStrengthScore * 0.10m
                          + (100 - signal.RiskScore) * 0.10m
                          + (leaderPos.Role == LeaderRole.Leader ? 15m : leaderPos.Role == LeaderRole.Core ? 10m : 0m)
                          + (signal.Trend == Trend.Up ? 5m : 0m);

                var tier = score >= 80 ? "激进"
                    : score >= 68 ? "重点"
                    : score >= 50 ? "普通" : null;
                if (tier == null) { return null; }

                var item = new WatchPoolItem
                {
                    Code = c.Code, Name = c.Name, Price = price,
                    Sector = c.Sector, WatchPoolScore = Math.Round(score, 1),
                    SecondWaveProbability = plat?.SecondWaveProbability ?? 0,
                    LockPositionStrength = plat?.LockPositionStrength ?? 0,
                    ChipLockScore = chipLock,
                    RiskScore = signal.RiskScore, Decision = signal.Decision, Tier = tier,
                    ResonanceScore = signal.SectorResonance?.ResonanceScore ?? 50m,
                    LeaderRole = leaderPos.Role, SectorRank = c.SectorRank,
                    SectorEmotionLabel = leaderPos.SectorEmotionLabel,
                    IsMarketLeader = leaderPos.IsMarketLeader,
                    LeaderReason = leaderPos.LeaderReason,
                    IsValuePool = leaderPos.Role is LeaderRole.Leader or LeaderRole.Core && score >= 75,
                    SectorHeatScore = mainline?.HeatScore ?? 0,
                    IsMainstreamSector = (mainline?.HeatScore ?? 0) >= 80,
                    IsSectorDeclining = false,
                    Reason = BuildFriendlyReason(signal, mainline, c.Sector),
                    RiskWarning = signal.IsOverextended ? "高位偏离，追涨风险" : ""
                };

                // 风险标签
                var tags = _riskTag.Evaluate(bars, signal, null, (decimal?)null);
                if (tags.Any(t => t.Severity >= 2))
                {
                    item.RiskTags = tags.Where(t => t.Severity >= 2).ToList();
                    if (item.RiskTags.Any(t => t.Severity >= 3)) item.WatchPoolScore *= 0.85m;
                    else item.WatchPoolScore *= 0.92m;
                }
                if (item.WatchPoolScore < 45) return null;

                UpdateScanProgress(scanId, done, totalCount, matched, filtered, failed,
                    c.Name, c.Sector, _ds.Status.CurrentSource, failedSources, "Running");
                return item;
            }
            catch { Interlocked.Increment(ref failed); return null; }
            finally { semaphore.Release(); }
        });

        var all = await Task.WhenAll(tasks);
        var items = all.Where(x => x != null).Cast<WatchPoolItem>()
            .OrderByDescending(x => x.WatchPoolScore).ToList();

        // 分类
        var leaders = items.Where(x => x.LeaderRole is LeaderRole.Leader or LeaderRole.Core).Take(5).ToList();
        var followers = items.Where(x => x.LeaderRole is LeaderRole.Follower or LeaderRole.CatchUp).Take(8).ToList();
        var others = items.Where(x => !leaders.Contains(x) && !followers.Contains(x)).Take(7).ToList();

        var final = leaders.Concat(followers).Concat(others)
            .OrderByDescending(x => x.WatchPoolScore).Take(20).ToList();
        for (int i = 0; i < final.Count; i++) final[i].Rank = i + 1;

        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        Console.WriteLine($"[WatchPool] === 完成 {DateTime.Now:HH:mm:ss} ===");
        Console.WriteLine($"[WatchPool] 主线板块: {mainlines.Count}, 候选: {uniqueCandidates.Count}, 命中: {matched}, 入选: {final.Count}");
        Console.WriteLine($"[WatchPool] 龙头/中军: {leaders.Count}, 跟风/补涨: {followers.Count}, 耗时: {elapsed:F1}s");

        UpdateScanProgress(scanId, totalCount, totalCount, matched, filtered, failed,
            "完成", string.Join(",", mainlines.Take(3).Select(m => m.SectorName)),
            _ds.Status.CurrentSource, failedSources, "Completed");

        var result = new WatchPoolResult
        {
            Items = final,
            TotalCount = totalCount,
            ScannedCount = scanned,
            FailedCount = failed,
            FilteredCount = filtered,
            MatchedCount = matched,
            CurrentStockName = currentStock,
            ElapsedSeconds = Math.Round(elapsed, 1),
            DataSourceName = _ds.Status.CurrentSource,
            DataSourceFailCount = _ds.Status.FailCount,
            UsingCache = _ds.Status.UsingCache,
            SkippedCount = _ds.Status.SkippedCount,
            DataSourceWarnings = _ds.Status.Warnings,
            MainlineSectors = mainlines,
            MarketEmotion = marketCtxResult.EmotionCycle,
            MarketSummary = marketCtxResult.MarketSummary,
            MarketRisingCount = risingSnap,
            MarketFallingCount = fallingSnap,
            MarketLimitUpCount = limitUpSnap,
            MarketLimitDownCount = limitDownSnap,
            AvgGain = Math.Round(avgGain, 4)
        };
        await SaveLogAsync(result);
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════════

    private static WatchPoolResult EmptyResult(string msg, string? scanId)
    {
        Console.WriteLine($"[WatchPool] 终止: {msg}");
        UpdateScanProgress(scanId, 0, 0, 0, 0, 0, "-", "-", "-", 0, "Failed");
        if (scanId != null && ScanProgresses.TryGetValue(scanId, out var sp))
            ScanProgresses[scanId] = sp with { Message = msg, Status = "Failed" };
        return new WatchPoolResult
        {
            Items = [], TotalCount = 0, ScannedCount = 0, FailedCount = 0, FilteredCount = 0, MatchedCount = 0,
            ElapsedSeconds = 0, DataSourceName = "无", DataSourceFailCount = 1,
            DataSourceWarnings = new List<string> { msg },
            MarketSummary = msg
        };
    }

    private static List<MainlineSector> BuildMainlines(
        List<(string, MainstreamSectorResult)> results, int totalSectors)
    {
        return results
            .Where(r => r.Item2.SectorHeatScore > 0)
            .Select(r =>
            {
                var emotion = r.Item2.SectorHeatScore >= 80 ? MarketEmotionCycle.Climax
                    : r.Item2.SectorHeatScore >= 65 ? MarketEmotionCycle.Ferment
                    : r.Item2.IsDeclining ? MarketEmotionCycle.Decline
                    : MarketEmotionCycle.Launch;
                return new MainlineSector(
                    r.Item1, r.Item2.SectorHeatScore, 0,
                    r.Item2.LimitUpCount, r.Item2.RisingCount, 0,
                    r.Item2.SectorContinuity, emotion, "", ""
                );
            })
            .OrderByDescending(m => m.HeatScore)
            .ToList();
    }

    /// <summary>用快照数据评估板块热度</summary>
    private static MainstreamSectorResult EvaluateSectorFromSnapshot(string sector, List<MarketSnapshotItem> snaps)
    {
        if (snaps.Count == 0)
            return new MainstreamSectorResult(sector, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, "无数据");

        int total = snaps.Count, rising = 0, falling = 0, limitUp = 0, strong = 0;
        decimal sumGain = 0, maxGain = 0;
        foreach (var s in snaps)
        {
            var gain = s.ChangePct / 100m;
            sumGain += gain;
            if (gain > 0) rising++; else if (gain < 0) falling++;
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

    // ═══════════════════════════════════════════════════════════════
    // 本地 CSV 降级扫描
    // ═══════════════════════════════════════════════════════════════

    private async Task<WatchPoolResult?> ScanLocalCsvFiles(
        List<StockBar>? marketBars, bool fullMarket, List<string>? targetSectors,
        string? scanId, DateTime startTime, CancellationToken ct)
    {
        var csvDir = Path.Combine(_dataDir, "SystemStocks");
        if (!Directory.Exists(csvDir)) { Console.WriteLine($"[WatchPool] CSV目录不存在: {csvDir}"); return null; }

        var csvFiles = Directory.GetFiles(csvDir, "*.csv");
        var candidates = new List<(string Code, string Name)>();
        foreach (var file in csvFiles)
        {
            var code = Path.GetFileNameWithoutExtension(file);
            if (code.Length == 6 && SecurityTypeFilter.IsCommonAStock(code).IsValid)
                candidates.Add((code, code));
        }

        candidates = candidates.OrderBy(x => Guid.NewGuid()).Take(300).ToList();
        if (candidates.Count == 0) return null;

        int scanned = 0, failed = 0, filtered = 0, matched = 0, done = 0;
        int totalCount = candidates.Count;
        string? currentStock = null;
        var semaphore = new SemaphoreSlim(8);

        UpdateScanProgress(scanId, 0, totalCount, 0, 0, 0, "准备中(本地CSV)", "本地缓存", "本地CSV", 0, "Running");

        var tasks = candidates.Select(async s =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                Interlocked.Exchange(ref currentStock, s.Name);
                var csvPath = Path.Combine(csvDir, $"{s.Code}.csv");
                List<StockBar>? bars = null;
                try { bars = new DataImporter().ImportCsv(csvPath, s.Code, s.Code); } catch { }

                Interlocked.Increment(ref done);
                if (bars == null || bars.Count < 60) { Interlocked.Increment(ref filtered); return null; }

                Interlocked.Increment(ref scanned);
                var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
                var signal = signals.FirstOrDefault();
                if (signal == null) return null;

                var trendOk = signal.Trend == Trend.Up || (signal.MainUpPlatform?.IsMainUpPlatform == true);
                var riskOk = signal.RiskScore <= 65;
                if (!trendOk && !riskOk) return null;

                Interlocked.Increment(ref matched);
                var score = (signal.MainUpPlatform?.SecondWaveProbability ?? 50m) * 0.2m
                          + (signal.ChipControl?.ChipLockScore ?? 50m) * 0.2m
                          + (signal.SectorEmotion?.SectorStrengthScore ?? 50m) * 0.15m
                          + signal.IntradayStrengthScore * 0.15m
                          + (100 - signal.RiskScore) * 0.15m
                          + (signal.Trend == Trend.Up ? 10m : 0m);

                if (score < 50) return null;

                return new WatchPoolItem
                {
                    Code = s.Code, Name = s.Code, Price = bars[^1].Close,
                    WatchPoolScore = Math.Round(score, 1),
                    SecondWaveProbability = signal.MainUpPlatform?.SecondWaveProbability ?? 0,
                    ChipLockScore = signal.ChipControl?.ChipLockScore ?? 0,
                    RiskScore = signal.RiskScore, Decision = signal.Decision,
                    Tier = score >= 75 ? "激进" : score >= 65 ? "重点" : "普通",
                    Reason = $"本地缓存: 趋势{signal.Trend}, 风险{signal.RiskScore}分",
                    RiskWarning = "本地缓存数据，建议启动AKShare获取实时数据",
                    LeaderRole = LeaderRole.Edge, SectorEmotionLabel = "-"
                };
            }
            catch { Interlocked.Increment(ref failed); return null; }
            finally { semaphore.Release(); }
        });

        var all = await Task.WhenAll(tasks);
        var items = all.Where(x => x != null).Cast<WatchPoolItem>()
            .OrderByDescending(x => x.WatchPoolScore).Take(20).ToList();
        for (int i = 0; i < items.Count; i++) items[i].Rank = i + 1;

        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        Console.WriteLine($"[WatchPool] 本地CSV完成: {items.Count}只, {elapsed:F1}s");

        var result = new WatchPoolResult
        {
            Items = items, TotalCount = totalCount, ScannedCount = scanned,
            FailedCount = failed, FilteredCount = filtered, MatchedCount = matched,
            ElapsedSeconds = Math.Round(elapsed, 1),
            DataSourceName = "本地CSV",
            DataSourceWarnings = new List<string> { "数据来源: 本地CSV缓存，非实时数据" },
            MarketSummary = "无市场数据（本地CSV模式）"
        };
        await SaveLogAsync(result);
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // 友好的入选理由
    // ═══════════════════════════════════════════════════════════════

    private static string BuildFriendlyReason(StockSignal s, MainlineSector? mainline, string sector)
    {
        var parts = new List<string>();

        // 趋势描述
        if (s.Trend == Trend.Up)
            parts.Add(s.DetailedStage switch
            {
                DetailedTrendStage.MainUpEarly or DetailedTrendStage.MainUpMid => "主升趋势明确",
                DetailedTrendStage.TrendBuilding => "趋势逐步增强",
                DetailedTrendStage.Launch => "趋势启动初期",
                _ => "均线结构稳定"
            });
        else if (s.Trend == Trend.Sideways)
            parts.Add("缩量整理中");

        // 量价描述
        if (s.VolumeDescription.Contains("放量"))
            parts.Add("资金重新放量");
        else if (s.VolumeDescription.Contains("缩量"))
            parts.Add("缩量整固");

        // 风险描述
        if (s.RiskScore <= 30)
            parts.Add("风险可控");
        else if (s.RiskScore <= 50)
            parts.Add("风险适中");

        // 板块描述
        if (mainline != null && mainline.HeatScore >= 65)
            parts.Add($"主线情绪仍在");

        return parts.Count > 0 ? string.Join("，", parts) : "技术形态符合筛选条件";
    }

    // ═══════════════════════════════════════════════════════════════
    // 持久化
    // ═══════════════════════════════════════════════════════════════

    private async Task UpdateCacheAsync(List<WatchPoolItem> newItems, List<CandidateStockItem> existingCache)
    {
        var cacheMap = existingCache.ToDictionary(c => c.StockCode);
        foreach (var item in newItems)
        {
            if (cacheMap.TryGetValue(item.Code, out var existing))
            {
                existing.ConsecutiveAppearDays++;
                existing.LastScore = item.WatchPoolScore;
                existing.LastAnalyzeTime = DateTime.Today;
                existing.IsPreviousWatchPool = true;
            }
            else
            {
                cacheMap[item.Code] = new CandidateStockItem
                {
                    StockCode = item.Code, StockName = item.Name,
                    LastScore = item.WatchPoolScore, LastAnalyzeTime = DateTime.Today,
                    ConsecutiveAppearDays = 1, IsPreviousWatchPool = true
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
