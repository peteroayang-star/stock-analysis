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
    private readonly MarketIndexService _marketIndex;
    private readonly SparkAiService _spark;
    private readonly AiAnalysisCacheService _aiCache;
    private readonly TradingLogicEngine _tradingLogic = new();
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public StockController(StockAnalyzer analyzer, MarketDataService marketData,
        TencentRealTimeService realTime, FinanceDataService finance, SignalLogService log,
        MarketIndexService marketIndex, SparkAiService spark, AiAnalysisCacheService aiCache)
    {
        _analyzer = analyzer;
        _marketData = marketData;
        _realTime = realTime;
        _finance = finance;
        _log = log;
        _marketIndex = marketIndex;
        _spark = spark;
        _aiCache = aiCache;
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

    /// <summary>生成AI解读（默认关闭，仅用户点击按钮时调用）</summary>
    [HttpPost]
    public async Task<IActionResult> GenerateAiAnalysis([FromQuery] string stock)
    {
        if (string.IsNullOrWhiteSpace(stock))
            return Json(new { error = "缺少股票代码" });

        // 检查缓存：同一股票同一交易日不重复调用
        var cached = _aiCache.Get(stock, DateTime.Today);
        if (cached != null)
            return Json(new { analysis = cached, cached = true });

        // 重新运行分析获取结构化数据
        var (bars, error) = await _marketData.TryGetBarsAsync(stock);
        if (bars == null)
            return Json(new { error = error ?? "无法获取行情数据" });

        var marketBars = await _marketIndex.GetMarketBarsAsync();
        var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars, skipAmountFilter: true);
        var signal = signals.FirstOrDefault();
        if (signal == null)
            return Json(new { error = "未检测到有效信号" });

        var structured = BuildStructuredResult(signal);
        var analysis = await _spark.GenerateAnalysisAsync(structured);
        if (analysis != null)
            _aiCache.Set(stock, DateTime.Today, analysis);

        return Json(new { analysis, cached = false });
    }

    private static StructuredAnalysisResult BuildStructuredResult(StockSignal s)
    {
        string RiskLevelText(int score) => score <= 30 ? "低风险" : score <= 50 ? "中风险" : score <= 70 ? "高风险" : "极高风险";
        string DecisionText(Decision d) => d switch
        {
            Decision.Buy => "可以买入", Decision.TryBuy => "尝试买入", Decision.Watch => "观察等待",
            Decision.Hold => "持有不动", Decision.Reduce => "建议减仓", Decision.Sell => "止损离场",
            _ => "暂时观望"
        };
        string TrendText(Trend t) => t switch
        {
            Trend.Up => "上涨偏强", Trend.Down => "下跌偏弱", _ => "震荡中性"
        };

        return new StructuredAnalysisResult
        {
            StockCode = s.Code, StockName = s.Name,
            TrendState = TrendText(s.Trend),
            VolumeState = s.VolumeDescription,
            IntradayState = s.AttackWillDescription,
            RiskLevel = RiskLevelText(s.RiskScore), RiskScore = s.RiskScore,
            TrendRisk = s.TrendRisk, VolatilityRisk = s.VolatilityRisk, SentimentRisk = s.SentimentRisk,
            Decision = DecisionText(s.Decision),
            SupportPrice = s.SupportPrice ?? 0, StopLossPrice = s.StopLossPrice ?? 0,
            WatchPrice = s.WatchPrice ?? 0, TargetPrice = s.TargetPrice,
            CycleStage = s.CycleStage, SectorName = s.SectorEmotion?.SectorName,
            SectorEmotion = s.SectorEmotion?.Cycle.ToString(),
            ActionAdvice = s.ActionAdvice, IsEmotionLeader = s.IsEmotionLeader,
            LimitUpCountIn14Days = s.LimitUpCountIn14Days,
            SmartMoneyDescription = s.SmartMoneyDescription,
            MainUpPlatformSummary = s.MainUpPlatform?.Summary,
            IntradayStrengthScore = s.IntradayStrengthScore,
            Reasons = s.Reasons
        };
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

        List<StockBar>? marketBars = await _marketIndex.GetMarketBarsAsync();

        List<MinuteBar>? minuteBars = null;
        if (bars.Last().Date.Date >= DateTime.Today.AddDays(-1))
            minuteBars = await _marketData.TryGetMinuteBarsAsync(bars.Last().Code, bars.Last().Date);

        var signals = _analyzer.Analyze(bars, TradingMode.Candidate, marketBars, minuteBars, skipAmountFilter: true);
        ViewBag.ExcludeReason = _analyzer.LastExcludeReason;
        var ranked = _ranker.Rank(signals);
        var items = new List<SignalWithSuggestion>();
        foreach (var s in ranked)
        {
            var rt  = await _realTime.GetAsync(s.Code);
            var fin = await _finance.GetAsync(s.Code);
            var (finAdj, finReasons) = FinanceHelper.CalculateRiskAdjustment(fin);
            s.RiskScore = Math.Clamp(s.RiskScore + finAdj, 0, 100);

            // 交易逻辑分析
            s.TradingLogic = _tradingLogic.Analyze(s,
                sectorName: s.SectorEmotion?.SectorName,
                revenueYoy: fin?.RevenueYoy,
                profitYoy: fin?.ProfitYoy);

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
