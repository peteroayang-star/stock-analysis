using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Web.Models;

namespace StockAnalysis.Web.Controllers;

public class BacktestController : Controller
{
    private readonly DataImporter _importer;
    private readonly Backtester _backtester;

    public BacktestController(DataImporter importer, Backtester backtester)
    {
        _importer = importer;
        _backtester = backtester;
    }

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Index(IFormFile file, string code, string name)
    {
        if (file == null || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        { ModelState.AddModelError("", "请填写所有字段并上传文件"); return View(); }
        var tmp = Path.GetTempFileName();
        try
        {
            await using (var fs = System.IO.File.Create(tmp)) await file.CopyToAsync(fs);
            var bars = _importer.ImportCsv(tmp, code.Trim(), name.Trim());
            var results = _backtester.Run(bars);
            return View("Result", new BacktestViewModel { Results = results, Summary = _backtester.Summarize(results) });
        }
        finally { System.IO.File.Delete(tmp); }
    }
}
