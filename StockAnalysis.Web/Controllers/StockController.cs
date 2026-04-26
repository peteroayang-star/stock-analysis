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
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public StockController(StockAnalyzer analyzer, DataImporter importer, MarketDataService marketData)
    {
        _analyzer = analyzer;
        _importer = importer;
        _marketData = marketData;
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

        var signals = _analyzer.Analyze(bars, TradingMode.Candidate);
        var ranked = _ranker.Rank(signals);
        var items = ranked.Select(s => new SignalWithSuggestion
        {
            Signal = s,
            Suggestion = _reasoner.Suggestion(s.SignalType, s.Decision, s.Reasons)
        }).ToList();
        return View("Result", new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate });
    }
}
