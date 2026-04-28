using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class StockController : Controller
{
    private readonly StockAnalyzer _analyzer;
    private readonly DataImporter _importer;
    private readonly MarketDataService _marketData;
    private readonly TencentRealTimeService _realTime;
    private readonly FinanceDataService _finance;
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public StockController(StockAnalyzer analyzer, DataImporter importer, MarketDataService marketData, TencentRealTimeService realTime, FinanceDataService finance)
    {
        _analyzer = analyzer;
        _importer = importer;
        _marketData = marketData;
        _realTime = realTime;
        _finance = finance;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Index(string stock, string? dataSource, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(stock))
        { ModelState.AddModelError("", "请输入股票名称或代码"); return View(); }

        List<StockBar>? bars = null;

        if (dataSource == "csv")
        {
            if (file == null)
            { ModelState.AddModelError("", "请选择CSV文件"); return View(); }
            var tmp = Path.GetTempFileName();
            try
            {
                await using (var fs = System.IO.File.Create(tmp)) await file.CopyToAsync(fs);
                var isCode = stock.Trim().Length == 6 && stock.Trim().All(char.IsDigit);
                bars = _importer.ImportCsv(tmp, isCode ? stock.Trim() : "", isCode ? "" : stock.Trim());
            }
            finally { System.IO.File.Delete(tmp); }
        }
        else
        {
            var (result, error) = await _marketData.TryGetBarsAsync(stock);
            if (result == null)
            {
                ViewBag.NotFound = true;
                ViewBag.Stock = stock;
                ViewBag.ErrorMessage = error;
                return View();
            }
            bars = result;
        }

        // 获取大盘数据（上证指数000001）用于大盘弱势判断
        List<StockBar>? marketBars = null;
        try { (marketBars, _) = await _marketData.TryGetBarsAsync("000001"); } catch { }

        var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars);
        var ranked = _ranker.Rank(signals);
        var items = new List<SignalWithSuggestion>();
        foreach (var s in ranked)
        {
            var rt = await _realTime.GetAsync(s.Code);
            var fin = await _finance.GetAsync(s.Code);

            // 业绩风险调整
            int finAdj = 0;
            var finReasons = new List<string>();
            if (fin != null && fin.ProfitYoy.Length > 0)
            {
                var latestProfitYoy = fin.ProfitYoy[0];
                var latestRevenueYoy = fin.RevenueYoy.Length > 0 ? fin.RevenueYoy[0] : 0;
                if (latestProfitYoy < -20) { finAdj += 20; finReasons.Add($"净利润同比下滑{Math.Abs(latestProfitYoy):F1}%"); }
                else if (latestProfitYoy < 0)  { finAdj += 10; finReasons.Add($"净利润同比下滑{Math.Abs(latestProfitYoy):F1}%"); }
                else if (latestProfitYoy > 30) { finAdj -= 10; finReasons.Add($"净利润同比增长{latestProfitYoy:F1}%"); }
                if (latestRevenueYoy < -10)    { finAdj += 10; finReasons.Add($"营收同比下滑{Math.Abs(latestRevenueYoy):F1}%"); }
                else if (latestRevenueYoy > 20){ finAdj -= 5;  finReasons.Add($"营收同比增长{latestRevenueYoy:F1}%"); }
            }
            s.RiskScore = Math.Clamp(s.RiskScore + finAdj, 0, 100);

            items.Add(new SignalWithSuggestion
            {
                Signal = s,
                Suggestion = _reasoner.Suggestion(s.SignalType, s.Decision, s.Reasons),
                RealTime = rt,
                Finance = fin,
                FinanceRiskAdj = finAdj,
                FinanceReasons = finReasons
            });
        }
        return View("Result", new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate });
    }
}
