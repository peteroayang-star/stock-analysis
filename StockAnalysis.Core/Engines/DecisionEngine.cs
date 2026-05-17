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
        bool belowMA20, bool macdDead, bool belowStopLoss,
        MainUpPlatformResult? platform = null,
        DragonTigerBehaviorResult? dragonTiger = null,
        SectorEmotionResult? sectorEmotion = null,
        ChipControlResult? chipControl = null,
        SectorResonanceResult? sectorResonance = null)
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
        if (vol.State == VolumeState.VolumeStall || vol.State == VolumeState.VolumeDistribute)
            return (Decision.Ignore, "放量滞涨，禁止买入");
        if (signal == BuySignalType.None && trend == Trend.Sideways && !vol.HasEffectiveVolume)
            return (Decision.Ignore, "无信号+无趋势+无放量，中间态");
        // ────────────────────────────────────────────────────

        // ── 新引擎规则 ───────────────────────────────────────
        if (sectorEmotion?.Cycle == SectorEmotionCycle.Decline)
            return signal != BuySignalType.None
                ? (Decision.Watch, "板块情绪衰退，禁止买入")
                : (Decision.Ignore, "板块情绪衰退，无信号");

        if (sectorEmotion?.Cycle == SectorEmotionCycle.Climax && sectorEmotion.LeadingStockStrength > 0.07m)
            if (signal != BuySignalType.None)
                return (Decision.Watch, "板块情绪高潮，追高风险");

        if (dragonTiger?.IsOneDayTour == true && signal != BuySignalType.None)
            return (Decision.Watch, "龙虎榜一日游，主力离场");

        // 龙虎榜锁仓加权：游资接力+锁仓+净买入为正，允许提升观察等级
        bool dtBoost = dragonTiger is { IsHotMoneyRelay: true, IsOneDayTour: false, IsLockPosition: true }
                       && dragonTiger.NetBuyAmount > 0;

        bool chipBoost = chipControl is { ChipLockScore: >= 70, SupportStrength: >= 70 };

        if (platform?.IsMainUpPlatform == true
            && platform.LockPositionStrength >= 70
            && platform.SecondWaveProbability >= 65
            && riskScore <= 50
            && cycle.Cycle != MarketCycle.Distribute
            && cycle.Cycle != MarketCycle.End)
        {
            bool strongCombo = chipBoost || dtBoost;
            if (signal == BuySignalType.None)
                return (riskScore <= 30 && strongCombo ? Decision.Buy : Decision.Watch, "主升平台锁仓，等待突破");
            return (riskScore <= 30 ? Decision.Buy : Decision.TryBuy, "主升平台确认，二波概率高");
        }

        // 龙虎榜加权：无主升平台但游资锁仓，允许 Ignore→Watch
        if (dtBoost && signal == BuySignalType.None && trend == Trend.Up && riskScore <= _cfg.WatchMaxScore)
            return (Decision.Watch, "龙虎榜游资锁仓，关注突破");

        // ── 板块共振规则 ─────────────────────────────────────
        if (sectorResonance?.IsIndependentPump == true || sectorResonance?.IsFakeBreakoutRisk == true)
        {
            if (signal != BuySignalType.None)
                return (Decision.Watch, "孤立拉升/诱多风险，禁止买入");
            return (Decision.Ignore, "孤立拉升/诱多风险，无信号");
        }

        if (sectorResonance?.ResonanceScore >= 75 && sectorEmotion?.Cycle == SectorEmotionCycle.Consensus)
        {
            if (signal != BuySignalType.None && riskScore <= _cfg.BuyMaxScore)
                return (Decision.Buy, "板块强共振+一致，信号确认");
            if (signal == BuySignalType.None && trend == Trend.Up && riskScore <= _cfg.WatchMaxScore)
                return (Decision.TryBuy, "板块强共振+一致，趋势向上");
        }
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
