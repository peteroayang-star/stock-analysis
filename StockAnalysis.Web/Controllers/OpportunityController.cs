using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class OpportunityController : Controller
{
    private readonly StockAnalyzer _analyzer;
    private readonly MarketDataService _marketData;
    private readonly TencentRealTimeService _realTime;
    private readonly FinanceDataService _finance;
    private readonly SignalLogService _log;
    private readonly MarketIndexService _marketIndex;
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public OpportunityController(StockAnalyzer analyzer, MarketDataService marketData,
        TencentRealTimeService realTime, FinanceDataService finance, SignalLogService log,
        MarketIndexService marketIndex)
    {
        _analyzer = analyzer;
        _marketData = marketData;
        _realTime = realTime;
        _finance = finance;
        _log = log;
        _marketIndex = marketIndex;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Index(string stocks)
    {
        if (string.IsNullOrWhiteSpace(stocks))
        { ModelState.AddModelError("", "请输入至少一个股票代码或名称"); return View(); }

        var codes = stocks.Split(new[] { ',', '，', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        List<StockBar>? marketBars = await _marketIndex.GetMarketBarsAsync();

        var items = new List<SignalWithSuggestion>();
        var failed = new List<string>();
        var itemsLock = new object();
        var analyzeLock = new object();
        var semaphore = new SemaphoreSlim(5);

        var tasks = codes.Select(async code =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (bars, _) = await _marketData.TryGetBarsAsync(code);
                if (bars == null) { lock (failed) failed.Add(code); return; }

                List<MinuteBar>? minuteBars = null;
                if (bars.Last().Date.Date >= DateTime.Today.AddDays(-1))
                    minuteBars = await _marketData.TryGetMinuteBarsAsync(bars.Last().Code, bars.Last().Date);

                List<StockSignal> signals;
                lock (analyzeLock) { signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars, minuteBars); }
                var ranked = _ranker.Rank(signals);
                foreach (var s in ranked)
                {
                    var rt = await _realTime.GetAsync(s.Code);
                    var fin = await _finance.GetAsync(s.Code);
                    var (finAdj, finReasons) = FinanceHelper.CalculateRiskAdjustment(fin);
                    s.RiskScore = Math.Clamp(s.RiskScore + finAdj, 0, 100);
                    lock (itemsLock) items.Add(new SignalWithSuggestion
                    {
                        Signal = s,
                        Suggestion = _reasoner.Suggestion(s.SignalType, s.Decision, s.Reasons),
                        RealTime = rt, Finance = fin, FinanceRiskAdj = finAdj, FinanceReasons = finReasons
                    });
                }
            }
            catch { lock (failed) failed.Add(code); }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        // 排序：TryBuy > Buy > Watch > 其他；同级按风险分升序
        items = items.OrderBy(x => x.Signal.Decision switch {
            Decision.TryBuy => 0, Decision.Buy => 1, Decision.Watch => 2, _ => 3
        }).ThenBy(x => x.Signal.RiskScore).ToList();

        ViewBag.Failed = failed;
        _log.Append(items.Select(x => x.Signal));
        return View("Result", new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate });
    }
}
