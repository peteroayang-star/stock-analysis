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
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public OpportunityController(StockAnalyzer analyzer, MarketDataService marketData,
        TencentRealTimeService realTime, FinanceDataService finance, SignalLogService log)
    {
        _analyzer = analyzer;
        _marketData = marketData;
        _realTime = realTime;
        _finance = finance;
        _log = log;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Index(string stocks)
    {
        if (string.IsNullOrWhiteSpace(stocks))
        { ModelState.AddModelError("", "请输入至少一个股票代码或名称"); return View(); }

        var codes = stocks.Split(new[] { ',', '，', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        List<StockBar>? marketBars = null;
        try { (marketBars, _) = await _marketData.TryGetBarsAsync("000001"); } catch { }

        var items = new List<SignalWithSuggestion>();
        var failed = new List<string>();

        foreach (var code in codes)
        {
            var (bars, _) = await _marketData.TryGetBarsAsync(code);
            if (bars == null) { failed.Add(code); continue; }

            var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
            var ranked = _ranker.Rank(signals);
            foreach (var s in ranked)
            {
                var rt = await _realTime.GetAsync(s.Code);
                var fin = await _finance.GetAsync(s.Code);
                int finAdj = 0;
                var finReasons = new List<string>();
                if (fin != null && fin.ProfitYoy.Length > 0)
                {
                    var yoy = Math.Abs(fin.ProfitYoy[0]) > 1000 ? double.NaN : fin.ProfitYoy[0];
                    if (!double.IsNaN(yoy))
                    {
                        if (yoy < -20) { finAdj += 20; finReasons.Add($"净利润同比下滑{Math.Abs(yoy):F1}%"); }
                        else if (yoy < 0) { finAdj += 10; finReasons.Add($"净利润同比下滑{Math.Abs(yoy):F1}%"); }
                        else if (yoy > 30) { finAdj -= 10; finReasons.Add($"净利润同比增长{yoy:F1}%"); }
                    }
                }
                s.RiskScore = Math.Clamp(s.RiskScore + finAdj, 0, 100);
                items.Add(new SignalWithSuggestion
                {
                    Signal = s,
                    Suggestion = _reasoner.Suggestion(s.SignalType, s.Decision, s.Reasons),
                    RealTime = rt, Finance = fin, FinanceRiskAdj = finAdj, FinanceReasons = finReasons
                });
            }
        }

        // 排序：TryBuy > Buy > Watch > 其他；同级按风险分升序
        items = items.OrderBy(x => x.Signal.Decision switch {
            Decision.TryBuy => 0, Decision.Buy => 1, Decision.Watch => 2, _ => 3
        }).ThenBy(x => x.Signal.RiskScore).ToList();

        ViewBag.Failed = failed;
        _log.Append(items.Select(x => x.Signal));
        return View("Result", new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate });
    }
}
