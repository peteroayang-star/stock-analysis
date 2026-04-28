using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>股票分析主入口，整合信号检测、风险评分和交易决策</summary>
public class StockAnalyzer
{
    private readonly BuySignalDetector _signal;
    private readonly RiskScoreEngine _risk = new();
    private readonly DecisionEngine _decision;
    private readonly StockFilter _filter;
    private readonly IndicatorCalculator _calc = new();

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
        if (last < 19) return [];

        var bar = bars[last];
        var excludeReason = _filter.ShouldExclude(bar.Code, bar.Name, bars);
        if (excludeReason != null) return [];

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

        var dec = mode == TradingMode.Portfolio
            ? _decision.DecideHolding(riskScore)
            : _decision.DecideEntry(signalType, riskScore, trend);

        // 操作建议和仓位
        var (action, position) = GenerateAdvice(dec, signalType, riskScore, trend);

        return [new StockSignal
        {
            Code = bar.Code, Name = bar.Name, Date = bar.Date, Close = bar.Close,
            SignalType = signalType, RiskScore = riskScore, Decision = dec,
            Reasons = reasons,
            SupportPrice  = ind != null ? Math.Round(ind.MA20, 2) : null,
            StopLossPrice = ind != null ? Math.Round(ind.MA20 * 0.98m, 2) : null,
            WatchPrice    = ind != null ? Math.Round(ind.MA10 * 1.02m, 2) : null,
            Trend = trend,
            ActionAdvice = action,
            PositionPct = position
        }];
    }

    private (string action, int position) GenerateAdvice(Decision dec, BuySignalType signal, int risk, Trend trend)
    {
        return dec switch
        {
            Decision.Buy => signal == BuySignalType.VolumeBreakout
                ? ("倍量突破确认，可轻仓试错，突破后加仓", 30)
                : ("信号出现，可小仓位介入，观察后续走势", 20),
            Decision.Watch => ("暂时观望，等待更明确信号或风险降低", 0),
            Decision.Hold => ("持有不动，继续观察趋势", 0),
            Decision.Reduce => ("风险上升，建议减仓50%，保留底仓", 0),
            Decision.Sell => ("触发止损，立即清仓离场", 0),
            _ => ("不参与，无明确信号", 0)
        };
    }
}