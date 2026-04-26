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
    public List<StockSignal> Analyze(List<StockBar> bars, TradingMode mode = TradingMode.Candidate)
    {
        int last = bars.Count - 1;
        if (last < 19) return [];

        var bar = bars[last];
        var excludeReason = _filter.ShouldExclude(bar.Code, bar.Name, bars);
        if (excludeReason != null) return [];

        var (signalType, signalReason) = _signal.Detect(bars, last);
        var (riskScore, riskReasons) = _risk.Score(bars, last);
        var dec = mode == TradingMode.Portfolio
            ? _decision.DecideHolding(riskScore)
            : _decision.DecideEntry(signalType, riskScore);

        var ind = _calc.Calculate(bars, last);
        var reasons = new List<string>();
        if (signalReason != "") reasons.Add(signalReason);
        reasons.AddRange(riskReasons);

        return [new StockSignal
        {
            Code = bar.Code, Name = bar.Name, Date = bar.Date, Close = bar.Close,
            SignalType = signalType, RiskScore = riskScore, Decision = dec,
            Reasons = reasons,
            SupportPrice  = ind != null ? Math.Round(ind.MA20, 2) : null,
            StopLossPrice = ind != null ? Math.Round(ind.MA20 * 0.98m, 2) : null,
            WatchPrice    = ind != null ? Math.Round(ind.MA10 * 1.02m, 2) : null,
        }];
    }
}
