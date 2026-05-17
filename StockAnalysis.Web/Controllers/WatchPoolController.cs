using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class WatchPoolController : Controller
{
    private readonly DailyWatchPoolService _service;
    private readonly MarketIndexService _marketIndex;

    public WatchPoolController(DailyWatchPoolService service, MarketIndexService marketIndex)
    {
        _service = service;
        _marketIndex = marketIndex;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var history = await _service.LoadHistoryAsync();
        ViewBag.History = history;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Generate(CancellationToken ct)
    {
        var marketBars = await _marketIndex.GetMarketBarsAsync();
        var result = await _service.GenerateAsync(marketBars, ct: ct);
        return View("Result", result);
    }

    [HttpGet]
    public async Task<IActionResult> History(string date)
    {
        var history = await _service.LoadHistoryAsync();
        var item = history.FirstOrDefault(h => h.Date.ToString("yyyy-MM-dd") == date);
        if (item == null) return RedirectToAction("Index");
        return View("Result", item);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string date)
    {
        var history = await _service.LoadHistoryAsync();
        var result = string.IsNullOrEmpty(date)
            ? history.FirstOrDefault()
            : history.FirstOrDefault(h => h.Date.ToString("yyyy-MM-dd") == date);
        if (result == null) return NotFound();

        var lines = new List<string> { "排名,代码,名称,当前价,总市值(亿),综合分,二波概率,平台锁仓,筹码锁定,板块情绪,风险分,决策,层级,入选理由,风险提示" };
        foreach (var item in result.Items)
            lines.Add($"{item.Rank},{item.Code},{item.Name},{item.Price:F2},{item.MarketCap:F0},{item.WatchPoolScore:F1},{item.SecondWaveProbability:F0},{item.LockPositionStrength:F0},{item.ChipLockScore:F0},{item.SectorEmotion},{item.RiskScore},{item.Decision},{item.Tier},\"{item.Reason}\",\"{item.RiskWarning}\"");

        var csv = string.Join("\n", lines);
        return File(System.Text.Encoding.UTF8.GetBytes("\uFEFF" + csv), "text/csv",
            $"watchpool_{result.Date:yyyyMMdd}.csv");
    }
}
