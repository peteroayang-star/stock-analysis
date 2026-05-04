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
    public async Task<IActionResult> Index(string sector)
    {
        if (string.IsNullOrWhiteSpace(sector))
        { ModelState.AddModelError("", "请选择板块"); return View(); }

        var stocks = await _akShare.GetSectorStocksAsync(sector);
        if (stocks == null || stocks.Count == 0)
        { ModelState.AddModelError("", "未能获取板块成分股"); return View(); }

        List<StockBar>? marketBars = null;
        try { (marketBars, _) = await _marketData.TryGetBarsAsync("000001"); } catch { }

        var items = new List<ScreenerResultItem>();

        // 并发控制：最多5个同时请求
        var semaphore = new SemaphoreSlim(5);
        var tasks = stocks.Select(async s =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var (bars, _) = await _marketData.TryGetBarsAsync(s.Code);
                if (bars == null || bars.Count < 30) return null;

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
                    RealTime = rt
                };
            }
            catch { return null; }
            finally { semaphore.Release(); }
        });

        var results = await Task.WhenAll(tasks);
        items = results.Where(r => r != null).Cast<ScreenerResultItem>()
            .OrderBy(x => x.Signal.RiskScore).ToList();

        ViewBag.Sector = sector;
        ViewBag.Total = stocks.Count;
        return View("Result", items);
    }
}
