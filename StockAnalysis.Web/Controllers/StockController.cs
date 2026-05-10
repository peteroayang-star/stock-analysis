using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class StockController : Controller
{
    private readonly StockAnalyzer _analyzer;
    private readonly MarketDataService _marketData;
    private readonly TencentRealTimeService _realTime;
    private readonly FinanceDataService _finance;
    private readonly SignalLogService _log;
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public StockController(StockAnalyzer analyzer, MarketDataService marketData, TencentRealTimeService realTime, FinanceDataService finance, SignalLogService log)
    {
        _analyzer = analyzer;
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
                price     = rt.Price,
                changePct = rt.ChangePct,
                open      = rt.Open,
                high      = rt.High,
                low       = rt.Low,
                preClose  = rt.PreClose
            };
        }
        return Json(result);
    }

    // GET 方式直接访问结果页（支持 URL 分享 / 工具栏跳转）
    [HttpGet]
    public async Task<IActionResult> Result([FromQuery] string stock)
    {
        if (string.IsNullOrWhiteSpace(stock)) return RedirectToAction("Index");
        return await RunAnalysis(stock, partial: false);
    }

    // AJAX 局部刷新接口（返回 HTML 片段，不含 Layout）
    [HttpGet]
    public async Task<IActionResult> AnalyzePartial([FromQuery] string stock)
    {
        if (string.IsNullOrWhiteSpace(stock))
            return Content("<div class='alert alert-warning'>请输入股票名称或代码</div>", "text/html");
        return await RunAnalysis(stock, partial: true);
    }

    [HttpPost]
    public async Task<IActionResult> Index(string stock)
    {
        if (string.IsNullOrWhiteSpace(stock))
        { ModelState.AddModelError("", "请输入股票名称或代码"); return View(); }
        return await RunAnalysis(stock, partial: false);
    }

    private async Task<IActionResult> RunAnalysis(string stock, bool partial)
    {
        var (bars, error) = await _marketData.TryGetBarsAsync(stock);
        if (bars == null)
        {
            if (partial)
                return Content($"<div class='alert alert-warning'>未找到「{stock}」的行情数据：{error}</div>", "text/html");
            ViewBag.NotFound = true;
            ViewBag.Stock = stock;
            ViewBag.ErrorMessage = error;
            return View("Index");
        }

        List<StockBar>? marketBars = null;
        try { (marketBars, _) = await _marketData.TryGetBarsAsync("000001"); } catch { }

        List<MinuteBar>? minuteBars = null;
        if (bars.Last().Date.Date >= DateTime.Today.AddDays(-1))
            minuteBars = await _marketData.TryGetMinuteBarsAsync(bars.Last().Code, bars.Last().Date);

        var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars, minuteBars);
        ViewBag.ExcludeReason = _analyzer.LastExcludeReason;
        var ranked = _ranker.Rank(signals);
        var items = new List<SignalWithSuggestion>();
        foreach (var s in ranked)
        {
            var rt  = await _realTime.GetAsync(s.Code);
            var fin = await _finance.GetAsync(s.Code);

            int finAdj = 0;
            var finReasons = new List<string>();
            if (fin != null && fin.ProfitYoy.Length > 0)
            {
                var latestProfitYoy  = Math.Abs(fin.ProfitYoy[0]) > 1000 ? double.NaN : fin.ProfitYoy[0];
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
                    if (latestRevenueYoy < -10)     { finAdj += 10; finReasons.Add($"营收同比下滑{Math.Abs(latestRevenueYoy):F1}%"); }
                    else if (latestRevenueYoy > 20) { finAdj -= 5;  finReasons.Add($"营收同比增长{latestRevenueYoy:F1}%"); }
                }
            }
            s.RiskScore = Math.Clamp(s.RiskScore + finAdj, 0, 100);

            // 用实时价重算关键价位（避免本地CSV数据过期导致价位与现价严重偏离）
            if (rt != null && rt.Price > 0)
            {
                var p = rt.Price;
                s.StopLossPrice = Math.Round(p * 0.98m, 2);
                s.WatchPrice    = Math.Round(p * 1.03m, 2);
                if (s.TargetPrice.HasValue)
                    s.TargetPrice = Math.Round(p * 1.08m, 2);
                s.SupportPrice  = Math.Round(p * 0.95m, 2);
            }

            items.Add(new SignalWithSuggestion
            {
                Signal         = s,
                Suggestion     = _reasoner.Suggestion(s.SignalType, s.Decision, s.Reasons),
                RealTime       = rt,
                Finance        = fin,
                FinanceRiskAdj = finAdj,
                FinanceReasons = finReasons
            });
        }
        _log.Append(items.Select(x => x.Signal));

        var vm = new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate };
        if (partial)
        {
            // 返回不含 Layout 的 HTML 片段
            return PartialView("ResultPartial", vm);
        }
        return View("Result", vm);
    }
}
