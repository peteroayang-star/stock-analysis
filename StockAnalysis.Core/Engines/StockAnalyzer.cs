using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>股票分析主入口，整合信号检测、风险评分和交易决策</summary>
public class StockAnalyzer
{
    private readonly BuySignalDetector _signal;
    private readonly RiskScoreEngine _risk = new();
    private readonly DecisionEngine _decision;
    private readonly StockFilter _filter;
    private readonly StockStabilityFilter _stability = new();
    private readonly IndicatorCalculator _calc = new();

    /// <summary>最近一次分析被过滤的原因，null 表示未被过滤</summary>
    public string? LastExcludeReason { get; private set; }

    /// <param name="cfg">应用配置</param>
    public StockAnalyzer(AppConfig cfg)
    {
        _signal = new BuySignalDetector(cfg.Signal);
        _decision = new DecisionEngine(cfg.Risk);
        _filter = new StockFilter(cfg.Filter);
    }

    /// <summary>
    /// 分析一只股票的 K 线数据，返回交易决策
    /// </summary>
    /// <param name="bars">K 线列表（按日期升序，至少 20 根）</param>
    /// <param name="mode">交易模式：选股或持仓</param>
    /// <returns>信号列表（0 或 1 条）</returns>
    public List<StockSignal> Analyze(List<StockBar> bars, TradingMode mode = TradingMode.Candidate, List<StockBar>? marketBars = null)
    {
        int last = bars.Count - 1;
        if (last < 28) return [];

        var bar = bars[last];
        LastExcludeReason = null;
        var excludeReason = _filter.ShouldExclude(bar.Code, bar.Name, bars);
        if (excludeReason != null) { LastExcludeReason = excludeReason; return []; }

        var (signalType, signalReason) = _signal.Detect(bars, last);

        // 大盘弱势判断：上证指数MA5 < MA20
        bool marketWeak = false;
        if (marketBars != null && marketBars.Count >= 20)
        {
            var mi = marketBars.Count - 1;
            var mInd = _calc.Calculate(marketBars, mi);
            marketWeak = mInd != null && mInd.MA5 < mInd.MA20;
        }

        var (riskScore, riskReasons) = _risk.Score(bars, last, marketWeak);

        var ind = _calc.Calculate(bars, last);
        var reasons = new List<string>();
        if (signalReason != "") reasons.Add(signalReason);
        reasons.AddRange(riskReasons);

        // 趋势判断
        var trend = ind != null && ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20 ? Trend.Up
                  : ind != null && ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20 ? Trend.Down
                  : Trend.Sideways;

        // 趋势阶段判断
        var trendStage = TrendStage.Sideways;
        if (ind != null)
        {
            if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) trendStage = TrendStage.Down;
            else if (ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20)
            {
                // 用MA5斜率和价格位置区分主升阶段
                var prevInd = last >= 20 ? _calc.Calculate(bars, last - 1) : null;
                if (prevInd == null) trendStage = TrendStage.EarlyUp;
                else if (ind.MA5 > prevInd.MA5 * 1.005m) trendStage = TrendStage.MidUp;
                else if (bar.Close > ind.MA20 * 1.20m) trendStage = TrendStage.LateUp;
                else trendStage = TrendStage.EarlyUp;
            }
        }

        var dec = mode == TradingMode.Portfolio
            ? _decision.DecideHolding(riskScore)
            : _decision.DecideEntry(signalType, riskScore, trend, ind != null && bar.Close >= ind.MA10 * 1.02m);

        // 稳定性过滤：必要条件不满足或评分<60，降级决策
        if (mode == TradingMode.Candidate)
        {
            var (stabilityScore, passRequired) = _stability.Evaluate(bars, last);
            if (!passRequired || stabilityScore < 60)
            {
                if (dec == Decision.Buy || dec == Decision.TryBuy)
                    dec = stabilityScore < 40 ? Decision.Ignore : Decision.Watch;
                reasons.Add($"稳定性评分{stabilityScore}，信号降级");
            }
        }

        // 操作建议和仓位
        var (action, position) = GenerateAdvice(dec, signalType, riskScore, trend);

        // 检测14天内是否触及涨停（最高价涨幅≥9.5%）
        bool hadLimitUp = false;
        int checkDays = Math.Min(14, last);
        for (int i = last - checkDays + 1; i <= last; i++)
        {
            if (bars[i - 1].Close > 0 && (bars[i].High - bars[i - 1].Close) / bars[i - 1].Close >= 0.095m)
            { hadLimitUp = true; break; }
        }

        return [new StockSignal
        {
            Code = bar.Code, Name = bar.Name, Date = bar.Date, Close = bar.Close,
            SignalType = signalType, RiskScore = riskScore, Decision = dec,
            Reasons = reasons,
            SupportPrice  = ind != null ? Math.Round(ind.MA20, 2) : null,
            StopLossPrice = ind != null ? Math.Round(ind.MA20 * 0.98m, 2) : null,
            WatchPrice    = ind != null ? Math.Round(ind.MA10 * 1.02m, 2) : null,
            TargetPrice   = ind != null ? Math.Round(ind.MA10 * 1.08m, 2) : null,
            Trend = trend,
            TrendStage = trendStage,
            ActionAdvice = action,
            PositionPct = position,
            HadLimitUpIn14Days = hadLimitUp
        }];
    }

    private (string action, int position) GenerateAdvice(Decision dec, BuySignalType signal, int risk, Trend trend)
    {
        return dec switch
        {
            Decision.Buy => signal == BuySignalType.VolumeBreakout
                ? ("倍量突破确认，可轻仓试错，突破后加仓", 30)
                : signal == BuySignalType.TrendPullback
                ? ("上涨趋势仍在，回调未破关键均线，可轻仓试错，建议仓位20%-30%", 25)
                : ("信号出现，可小仓位介入，观察后续走势", 20),
            Decision.TryBuy => ("价格突破观察位且趋势向上，可小仓参与（10%-20%），严格止损", 15),
            Decision.Watch => ("暂时观望，等待更明确信号或风险降低", 0),
            Decision.Hold => ("持有不动，继续观察趋势", 0),
            Decision.Reduce => ("风险上升，建议减仓50%，保留底仓", 0),
            Decision.Sell => ("触发止损，立即清仓离场", 0),
            _ => ("不参与，无明确信号", 0)
        };
    }
}