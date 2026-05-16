using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class ScreenerController : Controller
{
    private readonly StockAnalyzer _analyzer;
    private readonly MarketDataService _marketData;
    private readonly TencentRealTimeService _realTime;
    private readonly AkShareDataService _akShare;
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DragonScreener _dragon = new();

    public ScreenerController(StockAnalyzer analyzer, MarketDataService marketData,
        TencentRealTimeService realTime, AkShareDataService akShare)
    {
        _analyzer = analyzer;
        _marketData = marketData;
        _realTime = realTime;
        _akShare = akShare;
    }

    private static readonly List<string> HotSectors =
    [
        "半导体", "半导体设备","半导体材料", "军工装备", "液冷",
        "机器人概念", "算力租赁", "电力", "共封装光学(CPO)", "存储芯片",
        "锂电池", "光伏", "风电整机", "储能", "商业航天", "锂",
        "消费电子", "苹果概念", "机器人", "稀土", "黄金"
    ];

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.Sectors = HotSectors;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Index(string sector, string mode = "dragon")
    {
        if (string.IsNullOrWhiteSpace(sector))
        { ModelState.AddModelError("", "请选择板块"); return View(); }

        var stocks = await _akShare.GetSectorStocksAsync(sector);
        if (stocks == null || stocks.Count == 0)
        { ModelState.AddModelError("", "未能获取板块成分股"); return View(); }

        List<StockBar>? marketBars = null;
        try { (marketBars, _) = await _marketData.TryGetBarsAsync("000001"); } catch { }

        var items = new List<ScreenerResultItem>();
        var semaphore = new SemaphoreSlim(5);
        var tasks = stocks.Select(async s =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (bars, _) = await _marketData.TryGetBarsAsync(s.Code);
                if (bars == null || bars.Count < 30) return null;

                if (mode == "mainupplatform")
                {
                    // 前置过滤：非ST、非科创(688)、非创业(300/301)、非北交(8/4)
                    if (s.Name.Contains("ST") || s.Name.Contains("退"))  return null;
                    if (s.Code.StartsWith("688") || s.Code.StartsWith("300") ||
                        s.Code.StartsWith("301") || s.Code.StartsWith("8") ||
                        s.Code.StartsWith("4"))  return null;

                    // 价格过滤：< 30 元
                    var latestClose = bars[^1].Close;
                    if (latestClose >= 30m) return null;

                    // 市值过滤：< 300 亿（异步，失败则跳过市值检查）
                    var mv = await _akShare.TryGetMarketCapAsync(s.Code);
                    if (mv.HasValue && mv.Value >= 300m) return null;

                    var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
                    var signal = signals.FirstOrDefault();
                    if (signal == null) return null;
                    var plat = signal.MainUpPlatform;
                    if (plat == null || !plat.IsMainUpPlatform) return null;
                    if (plat.LockPositionStrength < 60 || signal.ChipControl?.ChipLockScore < 60) return null;
                    if (signal.RiskScore > 50) return null;
                    var rt = await _realTime.GetAsync(s.Code);
                    return new ScreenerResultItem
                    {
                        Dragon = _dragon.Screen(bars) ?? new DragonScreener.DragonResult(s.Code, s.Name, DateTime.Today, 0, "", 0),
                        Signal = signal,
                        Suggestion = _reasoner.Suggestion(signal.SignalType, signal.Decision, signal.Reasons),
                        RealTime = rt,
                        IsMainUpPlatform = true,
                        LockPositionStrength = plat.LockPositionStrength,
                        SecondWaveProbability = plat.SecondWaveProbability,
                        SectorEmotionLabel = signal.SectorEmotion?.Cycle.ToString(),
                        ChipLockScore = signal.ChipControl?.ChipLockScore
                    };
                }
                else
                {
                    var dragon = _dragon.Screen(bars);
                    if (dragon == null) return null;
                    var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
                    var signal = signals.FirstOrDefault();
                    if (signal == null || signal.RiskScore > 50) return null;
                    var rt = await _realTime.GetAsync(s.Code);
                    return new ScreenerResultItem
                    {
                        Dragon = dragon,
                        Signal = signal,
                        Suggestion = _reasoner.Suggestion(signal.SignalType, signal.Decision, signal.Reasons),
                        RealTime = rt,
                        IsMainUpPlatform = signal.MainUpPlatform?.IsMainUpPlatform ?? false,
                        LockPositionStrength = signal.MainUpPlatform?.LockPositionStrength,
                        SecondWaveProbability = signal.MainUpPlatform?.SecondWaveProbability,
                        SectorEmotionLabel = signal.SectorEmotion?.Cycle.ToString(),
                        ChipLockScore = signal.ChipControl?.ChipLockScore
                    };
                }
            }
            catch { return null; }
            finally { semaphore.Release(); }
        });

        var results = await Task.WhenAll(tasks);
        items = mode == "mainupplatform"
            ? results.Where(r => r != null).Cast<ScreenerResultItem>()
                .OrderByDescending(x => x.SecondWaveProbability)
                .ThenByDescending(x => x.LockPositionStrength)
                .ThenBy(x => x.Signal.RiskScore).ToList()
            : results.Where(r => r != null).Cast<ScreenerResultItem>()
                .OrderBy(x => x.Signal.RiskScore).ToList();

        ViewBag.Sector = sector;
        ViewBag.Total = stocks.Count;
        ViewBag.Mode = mode;
        return View("Result", items);
    }
}
