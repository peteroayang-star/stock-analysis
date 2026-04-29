using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public enum MarketCycle
{
    Launch,       // 启动：底部放量突破，均线开始多头
    Diverge,      // 分歧：上涨中出现缩量或震荡，多空分歧
    Consensus,    // 一致：多头排列稳固，量价配合
    MainUp,       // 主升：加速上涨，量价齐升
    Distribute,   // 派发：高位放量滞涨或上影线增多
    End           // 结束：跌破均线，趋势终结
}

public record CycleResult(MarketCycle Cycle, string Description);

public class CycleDetector
{
    private readonly IndicatorCalculator _calc = new();

    public CycleResult Detect(List<StockBar> bars, int index, VolumeResult vol)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return new(MarketCycle.End, "数据不足");

        var bar = bars[index];
        bool bullish = ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20;
        bool bearish = ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20;
        bool aboveMA20 = bar.Close >= ind.MA20;
        bool highShadow = ind.UpperShadowRatio > 0.4m;

        // 结束：空头排列或跌破MA20
        if (bearish || !aboveMA20)
            return new(MarketCycle.End, "均线空头排列或跌破MA20");

        // 派发：高位放量滞涨或上影线偏长
        if (bullish && bar.Close > ind.MA20 * 1.15m &&
            (vol.State == VolumeState.VolumeStall || highShadow))
            return new(MarketCycle.Distribute, "高位放量滞涨或上影线偏长");

        // 主升：多头排列 + 放量上涨 + 价格加速
        if (bullish && vol.State == VolumeState.VolumeUp && ind.ChangeRate > 3m)
            return new(MarketCycle.MainUp, "多头排列放量加速上涨");

        // 一致：多头排列稳固，量价正常配合
        if (bullish && vol.HasEffectiveVolume && vol.State != VolumeState.VolumeStall)
            return new(MarketCycle.Consensus, "多头排列量价配合");

        // 分歧：多头排列但缩量或滞涨
        if (bullish && (vol.State == VolumeState.ShrinkUp || vol.State == VolumeState.NoVolume))
            return new(MarketCycle.Diverge, "多头排列但量能不足");

        // 启动：均线刚开始多头，底部放量
        var prevInd = index >= 1 ? _calc.Calculate(bars, index - 1) : null;
        if (prevInd != null && ind.MA5 > ind.MA10 && prevInd.MA5 <= prevInd.MA10 && vol.HasEffectiveVolume)
            return new(MarketCycle.Launch, "均线金叉启动，底部放量");

        return new(MarketCycle.Diverge, "震荡分歧");
    }
}
