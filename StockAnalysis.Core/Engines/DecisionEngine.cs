using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>交易决策引擎，先读取周期阶段，再应用最高优先级强制规则</summary>
public class DecisionEngine
{
    private readonly RiskConfig _cfg;

    public DecisionEngine(RiskConfig cfg) => _cfg = cfg;

    public (Decision decision, string? forceReason) DecideEntry(
        BuySignalType signal, int riskScore, Trend trend,
        bool aboveWatchPrice, CycleResult cycle, VolumeResult vol,
        bool belowMA20, bool macdDead, bool belowStopLoss)
    {
        // ── 最高优先级强制规则 ──────────────────────────────
        if (belowStopLoss)
            return (Decision.Ignore, "当前价≤止损位，强制空仓");
        if (belowMA20 && macdDead)
            return (Decision.Ignore, "跌破MA20+MACD死叉，弱势结构");
        if (cycle.Cycle == MarketCycle.End)
            return (Decision.Ignore, "周期=结束，禁止参与");
        if (cycle.Cycle == MarketCycle.Distribute && signal != BuySignalType.None)
            return (Decision.Watch, "周期=派发，禁止买入，仅观察");
        if (vol.State == VolumeState.VolumeStall)
            return (Decision.Ignore, "放量滞涨，禁止买入");
        if (signal == BuySignalType.None && trend == Trend.Sideways && !vol.HasEffectiveVolume)
            return (Decision.Ignore, "无信号+无趋势+无放量，中间态");
        // ────────────────────────────────────────────────────

        if (riskScore > _cfg.WatchMaxScore) return (Decision.Ignore, null);

        if (signal == BuySignalType.None)
        {
            if (trend == Trend.Up && riskScore <= 30 && aboveWatchPrice)
                return (Decision.TryBuy, null);
            return (trend == Trend.Up && riskScore <= _cfg.WatchMaxScore)
                ? (Decision.Watch, null) : (Decision.Ignore, null);
        }

        return (riskScore <= _cfg.BuyMaxScore ? Decision.Buy : Decision.Watch, null);
    }

    public Decision DecideHolding(int riskScore, CycleResult cycle, bool belowStopLoss)
    {
        if (belowStopLoss || cycle.Cycle == MarketCycle.End)   return Decision.Sell;
        if (cycle.Cycle == MarketCycle.Distribute)             return Decision.Reduce;
        if (riskScore > _cfg.SellScore)                        return Decision.Sell;
        if (riskScore > _cfg.WatchMaxScore)                    return Decision.Reduce;
        return Decision.Hold;
    }
}
