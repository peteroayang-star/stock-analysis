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
    private readonly SignalLogService _log;
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public StockController(StockAnalyzer analyzer, DataImporter importer, MarketDataService marketData, TencentRealTimeService realTime, FinanceDataService finance, SignalLogService log)
    {
        _analyzer = analyzer;
        _importer = importer;
        _marketData = marketData;
        _realTime = realTime;
        _finance = finance;
        _log = log;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> RealTimeQuotes([FromQuery] string codes)
    {
        if (string.IsNullOrWhiteSpace(codes)) return Json(new { });
        var result = new Dictionary<string, object?>();
        foreach (var code in codes.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var rt = await _realTime.GetAsync(code.Trim());
            result[code.Trim()] = rt == null ? null : new {
                price    = rt.Price,
                changePct = rt.ChangePct,
                open     = rt.Open,
                high     = rt.High,
                low      = rt.Low,
                preClose = rt.PreClose
            };
        }
        return Json(result);
    }

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
        ViewBag.ExcludeReason = _analyzer.LastExcludeReason;
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
                // 同比绝对值超过1000%视为基期异常（基期接近0），忽略该数据
                var latestProfitYoy = Math.Abs(fin.ProfitYoy[0]) > 1000 ? double.NaN : fin.ProfitYoy[0];
                var latestRevenueYoy = fin.RevenueYoy.Length > 0
                    ? (Math.Abs(fin.RevenueYoy[0]) > 1000 ? double.NaN : fin.RevenueYoy[0])
                    : double.NaN;

                if (!double.IsNaN(latestProfitYoy))
                {
                    if (latestProfitYoy < -20)     { finAdj += 20; finReasons.Add($"净利润同比下滑{Math.Abs(latestProfitYoy):F1}%"); }
                    else if (latestProfitYoy < 0)  { finAdj += 10; finReasons.Add($"净利润同比下滑{Math.Abs(latestProfitYoy):F1}%"); }
                    else if (latestProfitYoy > 30) { finAdj -= 10; finReasons.Add($"净利润同比增长{latestProfitYoy:F1}%"); }
                }
                if (!double.IsNaN(latestRevenueYoy))
                {
                    if (latestRevenueYoy < -10)      { finAdj += 10; finReasons.Add($"营收同比下滑{Math.Abs(latestRevenueYoy):F1}%"); }
                    else if (latestRevenueYoy > 20)  { finAdj -= 5;  finReasons.Add($"营收同比增长{latestRevenueYoy:F1}%"); }
                }
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
        _log.Append(items.Select(x => x.Signal));
        return View("Result", new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate });
    }
}
