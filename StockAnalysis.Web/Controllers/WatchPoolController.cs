using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Web.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class WatchPoolController : Controller
{
    private readonly DailyWatchPoolService _service;
    private readonly MarketIndexService _marketIndex;
    private readonly AkShareDataService _akShare;

    public WatchPoolController(DailyWatchPoolService service, MarketIndexService marketIndex,
        AkShareDataService akShare)
    {
        _service = service;
        _marketIndex = marketIndex;
        _akShare = akShare;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var history = await _service.LoadHistoryAsync();
        ViewBag.History = history;
        // 加载热门板块列表供选择
        ViewBag.HotSectors = await _akShare.GetSectorsAsync();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Generate(string? sectors, bool advanced = false, CancellationToken ct = default)
    {
        var marketBars = await _marketIndex.GetMarketBarsAsync();

        // 确定扫描范围
        List<string>? targetSectors = null;
        bool fullMarket = advanced;

        if (!string.IsNullOrWhiteSpace(sectors))
        {
            // 用户选了特定板块
            targetSectors = sectors.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        }
        else if (!advanced)
        {
            // 用户没选板块且非高级模式 → 自动加载全部热门板块
            var allSectors = await _akShare.GetSectorsAsync();
            if (allSectors.Count == 0)
            {
                // 无法获取板块列表，降级为全市场模式
                Console.WriteLine("[WatchPool] 无法获取热门板块列表，降级为全市场扫描");
                fullMarket = true;
            }
            else
            {
                targetSectors = allSectors.Take(10).ToList();
                Console.WriteLine($"[WatchPool] 自动选择 {targetSectors.Count} 个热门板块");
            }
        }
        // else: advanced=true && sectors为空 → fullMarket=true（全市场）

        var scanId = Guid.NewGuid().ToString("N")[..8];

        // 立即初始化进度，避免前端首次轮询拿到空
        DailyWatchPoolService.InitScanProgress(scanId);

        // 后台启动扫描
        var capturedSectors = targetSectors;
        var capturedFullMarket = fullMarket;
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.GenerateAsync(marketBars, capturedSectors, capturedFullMarket, scanId, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WatchPool] 扫描异常: {ex.Message}");
                DailyWatchPoolService.UpdateScanProgressFail(scanId, $"扫描异常: {ex.Message}");
            }
        }, ct);

        return Json(new { scanId, message = "扫描已启动" });
    }

    [HttpGet]
    public IActionResult Progress(string scanId)
    {
        var p = DailyWatchPoolService.GetScanProgress(scanId);
        // 返回完整默认值，避免前端显示 undefined
        if (p == null) return Json(new
        {
            done = false, processed = 0, total = 0, matched = 0,
            filtered = 0, failed = 0, currentStock = "准备中...",
            currentSector = "", currentSource = "", failedSources = 0,
            status = "Idle", message = (string?)null, percent = 0
        });
        return Json(new
        {
            done = p.IsDone,
            processed = p.Processed,
            total = p.Total,
            matched = p.Matched,
            filtered = p.Filtered,
            failed = p.Failed,
            currentStock = p.CurrentStock,
            currentSector = p.CurrentSector,
            currentSource = p.CurrentSource,
            failedSources = p.FailedSources,
            status = p.Status,
            message = p.Message,
            percent = p.Percent
        });
    }

    [HttpGet]
    public async Task<IActionResult> Result(string? date)
    {
        if (!string.IsNullOrEmpty(date))
        {
            var history = await _service.LoadHistoryAsync();
            var item = history.FirstOrDefault(h => h.Date.ToString("yyyy-MM-dd") == date);
            if (item != null) return View(item);
        }
        // 取最新记录
        var latest = (await _service.LoadHistoryAsync()).FirstOrDefault();
        if (latest == null) return RedirectToAction("Index");
        return View(latest);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string date)
    {
        var history = await _service.LoadHistoryAsync();
        var result = string.IsNullOrEmpty(date)
            ? history.FirstOrDefault()
            : history.FirstOrDefault(h => h.Date.ToString("yyyy-MM-dd") == date);
        if (result == null) return NotFound();

        var lines = new List<string> { "排名,代码,名称,当前价,总市值(亿),综合分,二波概率,平台锁仓,筹码锁定,板块情绪,风险分,决策,层级,风险标签,入选理由,风险提示" };
        foreach (var item in result.Items)
            lines.Add($"{item.Rank},{item.Code},{item.Name},{item.Price:F2},{item.MarketCap:F0},{item.WatchPoolScore:F1},{item.SecondWaveProbability:F0},{item.LockPositionStrength:F0},{item.ChipLockScore:F0},{item.SectorEmotion},{item.RiskScore},{item.Decision},{item.Tier},{item.RiskTagSummary},\"{item.Reason}\",\"{item.RiskWarning}\"");

        var csv = string.Join("\n", lines);
        return File(System.Text.Encoding.UTF8.GetBytes("\uFEFF" + csv), "text/csv",
            $"watchpool_{result.Date:yyyyMMdd}.csv");
    }
}
