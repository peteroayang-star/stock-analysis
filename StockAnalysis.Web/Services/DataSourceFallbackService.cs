using System.Globalization;
using System.Text;
using System.Text.Json;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>全市场行情快照条目（从AKShare批量接口获取）</summary>
public record MarketSnapshotItem(
    string Code, string Name,
    decimal Price, decimal ChangePct,
    decimal Amount, decimal TotalMv,
    decimal Turnover);

/// <summary>数据源状态追踪</summary>
public class DataSourceStatus
{
    public string CurrentSource { get; set; } = "AKShare";
    public int FailCount { get; set; }
    public bool UsingCache { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Warnings { get; } = [];

    public void Warn(string msg) { Warnings.Add(msg); FailCount++; }
}

public class DataSourceFallbackService
{
    private readonly AkShareDataService _akShare;
    private readonly TencentRealTimeService _tencent;
    private readonly DataImporter _importer;
    private readonly string _dataDir;

    // 内存缓存：全市场快照（交易时间内3分钟有效，非交易时间到次日开盘）
    private List<MarketSnapshotItem>? _snapshotCache;
    private DateTime _snapshotCacheTime = DateTime.MinValue;

    // 板块成分股缓存（1天有效）
    private readonly string _sectorCachePath;

    public DataSourceStatus Status { get; } = new();

    public DataSourceFallbackService(AkShareDataService akShare, TencentRealTimeService tencent,
        DataImporter importer, IWebHostEnvironment env)
    {
        _akShare = akShare;
        _tencent = tencent;
        _importer = importer;
        _dataDir = Path.Combine(env.ContentRootPath, "..", "Data");
        _sectorCachePath = Path.Combine(_dataDir, "sector_cache.json");
        Directory.CreateDirectory(_dataDir);
    }

    /// <summary>一次性获取全市场行情快照，内存缓存3分钟</summary>
    public async Task<List<MarketSnapshotItem>> GetMarketSnapshotAsync()
    {
        var now = DateTime.Now;
        bool inTradingHours = now.TimeOfDay >= TimeSpan.FromHours(9.5) && now.TimeOfDay <= TimeSpan.FromHours(15);
        var cacheExpiry = inTradingHours ? TimeSpan.FromMinutes(3) : TimeSpan.FromHours(18);

        if (_snapshotCache != null && (now - _snapshotCacheTime) < cacheExpiry)
            return _snapshotCache;

        try
        {
            // 通过 Python 服务获取全市场快照
            var json = await _akShare.GetRawAsync("http://127.0.0.1:5100/snapshot");
            var doc = JsonDocument.Parse(json).RootElement;
            var items = doc.GetProperty("stocks").EnumerateArray().Select(x => new MarketSnapshotItem(
                x.GetProperty("code").GetString()!,
                x.GetProperty("name").GetString()!,
                x.TryGetProperty("price", out var p) ? p.GetDecimal() : 0,
                x.TryGetProperty("change_pct", out var cp) ? cp.GetDecimal() : 0,
                x.TryGetProperty("amount", out var a) ? a.GetDecimal() : 0,
                x.TryGetProperty("total_mv", out var mv) ? mv.GetDecimal() : 0,
                x.TryGetProperty("turnover", out var t) ? t.GetDecimal() : 0
            )).ToList();

            _snapshotCache = items;
            _snapshotCacheTime = now;
            Status.CurrentSource = "AKShare/东方财富";
            Status.UsingCache = false;
            return items;
        }
        catch (Exception ex)
        {
            Status.Warn($"全市场快照失败: {ex.Message}");
            Status.UsingCache = true;
            Status.CurrentSource = "缓存";
            return _snapshotCache ?? [];
        }
    }

    /// <summary>获取单股实时行情：AKShare → 腾讯 → null</summary>
    public async Task<RealTimeQuote?> GetRealtimeQuoteAsync(string code)
    {
        // 快照仅有价格和涨跌幅，无真实OHLC数据，不伪造K线
        // 跳过快照查询，直接走腾讯接口获取完整行情

        // 腾讯接口
        try
        {
            var qt = await _tencent.GetAsync(code);
            if (qt != null) { Status.CurrentSource = "腾讯"; return qt; }
        }
        catch (Exception ex) { Status.Warn($"腾讯行情失败[{code}]: {ex.Message}"); }

        Status.SkippedCount++;
        return null;
    }

    /// <summary>获取历史K线：本地缓存 → AKShare → 跳过</summary>
    public async Task<List<StockBar>?> GetHistoryBarsAsync(string code, string name)
    {
        try
        {
            var bars = await _akShare.TryGetBarsAsync(code, name);
            if (bars != null) return bars;
        }
        catch (Exception ex) { Status.Warn($"历史K线失败[{code}]: {ex.Message}"); }

        Status.SkippedCount++;
        return null;
    }

    /// <summary>获取市值：优先从快照读，再调接口，失败返回null</summary>
    public async Task<decimal?> GetMarketCapAsync(string code)
    {
        if (_snapshotCache != null)
        {
            var snap = _snapshotCache.FirstOrDefault(s => s.Code == code);
            if (snap != null && snap.TotalMv > 0) return snap.TotalMv / 1_0000_0000m;
        }
        try { return await _akShare.TryGetMarketCapAsync(code); }
        catch { return null; }
    }

    /// <summary>获取板块成分股：AKShare → 本地缓存</summary>
    public async Task<List<(string Code, string Name)>> GetSectorStocksAsync(string sectorName)
    {
        try
        {
            var stocks = await _akShare.GetSectorStocksAsync(sectorName);
            if (stocks.Count > 0)
            {
                await SaveSectorCacheAsync(sectorName, stocks);
                return stocks;
            }
        }
        catch (Exception ex) { Status.Warn($"板块成分股失败[{sectorName}]: {ex.Message}"); }

        // 读缓存
        var cached = await LoadSectorCacheAsync(sectorName);
        if (cached.Count > 0) { Status.UsingCache = true; return cached; }
        return [];
    }

    /// <summary>获取板块列表：AKShare → 缓存</summary>
    public async Task<List<string>> GetSectorsAsync()
    {
        try
        {
            var sectors = await _akShare.GetSectorsAsync();
            if (sectors.Count > 0)
            {
                await SaveSectorListCacheAsync(sectors);
                return sectors;
            }
        }
        catch (Exception ex) { Status.Warn($"板块列表失败: {ex.Message}"); }

        Status.UsingCache = true;
        return await LoadSectorListCacheAsync();
    }

    // ── 板块缓存 ──────────────────────────────────────────────

    private async Task SaveSectorCacheAsync(string sector, List<(string Code, string Name)> stocks)
    {
        try
        {
            var cache = await LoadAllSectorCacheAsync();
            cache[sector] = new SectorCacheEntry(stocks.Select(s => new SectorStock(s.Code, s.Name)).ToList(), DateTime.Today);
            await File.WriteAllTextAsync(_sectorCachePath,
                JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { }
    }

    private async Task<List<(string Code, string Name)>> LoadSectorCacheAsync(string sector)
    {
        var cache = await LoadAllSectorCacheAsync();
        if (cache.TryGetValue(sector, out var entry) && entry.Date >= DateTime.Today.AddDays(-1))
            return entry.Stocks.Select(s => (s.Code, s.Name)).ToList();
        return [];
    }

    private async Task<Dictionary<string, SectorCacheEntry>> LoadAllSectorCacheAsync()
    {
        if (!File.Exists(_sectorCachePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_sectorCachePath);
            return JsonSerializer.Deserialize<Dictionary<string, SectorCacheEntry>>(json) ?? [];
        }
        catch { return []; }
    }

    private async Task SaveSectorListCacheAsync(List<string> sectors)
    {
        try { await File.WriteAllTextAsync(Path.Combine(_dataDir, "sector_list_cache.json"), JsonSerializer.Serialize(sectors)); }
        catch { }
    }

    private async Task<List<string>> LoadSectorListCacheAsync()
    {
        var path = Path.Combine(_dataDir, "sector_list_cache.json");
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(path)) ?? []; }
        catch { return []; }
    }

    private record SectorCacheEntry(List<SectorStock> Stocks, DateTime Date);
    private record SectorStock(string Code, string Name);
}
