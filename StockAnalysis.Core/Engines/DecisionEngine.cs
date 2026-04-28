using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>交易决策引擎，根据信号类型和风险分给出最终操作建议</summary>
public class DecisionEngine
{
    private readonly RiskConfig _cfg;

    /// <param name="cfg">风险评分阈值配置</param>
    public DecisionEngine(RiskConfig cfg) => _cfg = cfg;

    /// <summary>
    /// 选股模式决策：判断是否买入
    /// </summary>
    /// <param name="signal">检测到的买入信号类型</param>
    /// <param name="riskScore">风险评分（0-100）</param>
    /// <returns>Buy / Watch / Ignore</returns>
    public Decision DecideEntry(BuySignalType signal, int riskScore, Trend trend = Trend.Sideways)
    {
        if (riskScore > _cfg.WatchMaxScore) return Decision.Ignore;
        if (signal == BuySignalType.None)
            return (trend == Trend.Up && riskScore <= _cfg.WatchMaxScore) ? Decision.Watch : Decision.Ignore;
        return riskScore <= _cfg.BuyMaxScore ? Decision.Buy : Decision.Watch;
    }

    /// <summary>
    /// 持仓模式决策：判断持有/减仓/止损
    /// </summary>
    /// <param name="riskScore">风险评分（0-100）</param>
    /// <returns>Hold / Reduce / Sell</returns>
    public Decision DecideHolding(int riskScore)
    {
        if (riskScore > _cfg.SellScore) return Decision.Sell;
        if (riskScore > _cfg.WatchMaxScore) return Decision.Reduce;
        return Decision.Hold;
    }
}
