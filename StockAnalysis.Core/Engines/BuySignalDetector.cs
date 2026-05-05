using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>买入信号检测器，依赖 VolumeResult 和 CycleResult，不再自行判断量价</summary>
public class BuySignalDetector
{
    private readonly IndicatorCalculator _calc = new();
    private readonly SignalConfig _cfg;

    public BuySignalDetector(SignalConfig cfg) => _cfg = cfg;

    public (BuySignalType type, string reason) Detect(
        List<StockBar> bars, int index,
        VolumeResult vol, CycleResult cycle)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return (BuySignalType.None, "");

        var bar = bars[index];

        // 派发/结束周期禁止发出买入信号
        if (cycle.Cycle == MarketCycle.Distribute || cycle.Cycle == MarketCycle.End)
            return (BuySignalType.None, "");

        // 倍量突破：收盘突破近20日高点 + 有效放量 + 阳线
        if (bar.Close > ind.High20 && vol.HasEffectiveVolume &&
            bar.Volume >= (double)(ind.VolMA5 * (decimal)_cfg.VolumeMultiplier) &&
            bar.Close > bar.Open)
            return (BuySignalType.VolumeBreakout, $"收盘突破近{_cfg.BreakoutDays}日高点，放量阳线确认");

        // MACD金叉
        var prevInd = _calc.Calculate(bars, index - 1);
        if (prevInd != null && ind.DIF > ind.DEA && prevInd.DIF <= prevInd.DEA && ind.MACD > 0)
            return (BuySignalType.PullbackSupport, "MACD金叉，DIF上穿DEA");

        // MACD死叉时，只有满足"趋势回调修复结构"才允许发出信号（否则降级为无信号）
        bool macdDead = prevInd != null && ind.DIF < ind.DEA;
        bool isRepairStructure = ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20  // 多头排列未破
            && (vol.State == VolumeState.ShrinkConsolidate || vol.State == VolumeState.ShrinkPullback) // 缩量
            && bar.Close >= ind.MA20;  // 守住MA20

        // 缩量回踩MA10：多头排列 + 缩量 + 贴近MA10 + 收阳（MACD死叉时须满足修复结构）
        if (ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20
            && (vol.State == VolumeState.ShrinkConsolidate || vol.State == VolumeState.ShrinkPullback)
            && Math.Abs(bar.Close - ind.MA10) / ind.MA10 <= _cfg.PullbackNearMARatio
            && bar.Close >= bar.Open
            && (!macdDead || isRepairStructure))
            return (BuySignalType.PullbackSupport, $"多头排列缩量回踩MA10（±{_cfg.PullbackNearMARatio:P0}）");

        // 凹量洗盘：连续N日缩量 + 收盘守住MA20（MACD死叉时须满足修复结构）
        if (index >= 19 + _cfg.WashoutDays
            && Enumerable.Range(1, _cfg.WashoutDays).All(n => bars[index - n].Volume < bars[index - n - 1].Volume)
            && bar.Close >= ind.MA20
            && (!macdDead || isRepairStructure))
            return (BuySignalType.VolumeWashout, $"连续{_cfg.WashoutDays}日缩量洗盘，收盘守住MA20");

        // 趋势回调：多头排列 + 近3日缩量 + 未破MA20 + 当日转强（MACD死叉时须满足修复结构）
        if (index >= 22
            && ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20
            && bar.Close >= ind.MA20
            && bars[index - 1].Volume > bars[index].Volume
            && bars[index - 2].Volume > bars[index - 1].Volume
            && (bar.Close >= bar.Open || bar.Close >= ind.MA5)
            && (!macdDead || isRepairStructure))
        {
            var prevBar = bars[index - 1];
            if (prevBar.Close < ind.MA5 || prevBar.Close < ind.MA10)
                return (BuySignalType.TrendPullback, "均线多头排列，缩量回调后转强");
        }

        return (BuySignalType.None, "");
    }
}
