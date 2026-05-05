using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class BacktestController : Controller
{
    private readonly Backtester _backtester;
    private readonly MarketDataService _marketData;

    public BacktestController(Backtester backtester, MarketDataService marketData)
    {
        _backtester = backtester;
        _marketData = marketData;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Index(string stock)
    {
        if (string.IsNullOrWhiteSpace(stock))
        { ModelState.AddModelError("", "请输入股票名称或代码"); return View(); }

        var (result, error) = await _marketData.TryGetBarsAsync(stock);
        if (result == null)
        {
            ViewBag.NotFound = true;
            ViewBag.Stock = stock;
            ViewBag.ErrorMessage = error;
            return View();
        }

        var results = _backtester.Run(result);
        return RedirectToAction("Result", new { stock });
    }

    [HttpGet]
    public async Task<IActionResult> Result(string stock, string sortBy = "date", string filterSig = "")
    {
        if (string.IsNullOrWhiteSpace(stock)) return RedirectToAction("Index");
        var (result, _) = await _marketData.TryGetBarsAsync(stock);
        if (result == null) return RedirectToAction("Index");
        var results = _backtester.Run(result);
        ViewBag.Stock = stock;
        ViewBag.SortBy = sortBy;
        ViewBag.FilterSig = filterSig;
        return View(new BacktestViewModel { Results = results, Summary = _backtester.Summarize(results) });
    }
}
