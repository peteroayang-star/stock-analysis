using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class BacktestController : Controller
{
    private readonly DataImporter _importer;
    private readonly Backtester _backtester;
    private readonly MarketDataService _marketData;

    public BacktestController(DataImporter importer, Backtester backtester, MarketDataService marketData)
    {
        _importer = importer;
        _backtester = backtester;
        _marketData = marketData;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Index(string stock, string? dataSource, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(stock))
        { ModelState.AddModelError("", "请输入股票名称或代码"); return View(); }

        Core.Models.StockBar[]? bars = null;

        if (dataSource == "csv")
        {
            if (file == null)
            { ModelState.AddModelError("", "请选择CSV文件"); return View(); }
            var tmp = Path.GetTempFileName();
            try
            {
                await using (var fs = System.IO.File.Create(tmp)) await file.CopyToAsync(fs);
                var isCode = stock.Trim().Length == 6 && stock.Trim().All(char.IsDigit);
                bars = _importer.ImportCsv(tmp, isCode ? stock.Trim() : "", isCode ? "" : stock.Trim()).ToArray();
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
            bars = result.ToArray();
        }

        var results = _backtester.Run(bars.ToList());
        return View("Result", new BacktestViewModel { Results = results, Summary = _backtester.Summarize(results) });
    }
}
