using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public enum VolumeState
{
    AggressiveBuy,       // 放量进攻：放量+收阳+收盘接近最高价
    ShrinkConsolidate,   // 缩量整理：缩量+未跌破均线+波动收窄
    ShrinkPullback,      // 缩量回踩：缩量+回踩中（价格下行）
    VolumeStall,         // 放量滞涨：放量但涨幅很小+上影明显
    VolumeDistribute,    // 放量派发：高位放巨量+冲高回落+长上影
    WeakSupport,         // 弱承接：缩量下跌+无主动拉升
    NoVolumeSideways,    // 无量横盘：无量+无趋势+无突破
    VolumeDivergence     // 资金分歧：量价关系不明确
}

public record VolumeResult(
    VolumeState State,
    bool HasEffectiveVolume,
    string Description
);

public class VolumeEngine
{
    private readonly IndicatorCalculator _calc = new();

    public VolumeResult Analyze(List<StockBar> bars, int index)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return new(VolumeState.VolumeDivergence, false, "资金分歧");

        var bar = bars[index];
        var vol = (decimal)bar.Volume;
        bool hasVolume = vol > ind.VolMA5;
        bool bigVolume = vol >= ind.VolMA5 * 1.5m;
        bool shrink = vol < ind.VolMA5 * 0.8m;
        bool isUp = ind.ChangeRate > 0;
        bool isDown = ind.ChangeRate < -0.5m;
        bool stall = hasVolume && Math.Abs(ind.ChangeRate) < 1m;
        bool highShadow = ind.UpperShadowRatio > 0.4m;
        bool nearHigh = bar.High > 0 && (bar.Close - bar.Low) / (bar.High - bar.Low + 0.001m) > 0.7m;
        bool highPosition = bar.Close > ind.MA20 * 1.15m;

        // 放量派发：高位 + 放巨量 + 冲高回落（长上影）
        if (highPosition && bigVolume && highShadow && ind.ChangeRate < 3m)
            return new(VolumeState.VolumeDistribute, true, "放量派发");

        // 放量进攻：放量 ≥ 1.5倍均量 + 收阳 + 收盘接近最高价
        if (bigVolume && isUp && nearHigh)
            return new(VolumeState.AggressiveBuy, true, "放量进攻");

        // 放量滞涨：放量 + 涨幅很小 + 上影明显
        if (stall && highShadow)
            return new(VolumeState.VolumeStall, true, "放量滞涨");

        // 弱承接：缩量下跌
        if (shrink && isDown)
            return new(VolumeState.WeakSupport, false, "弱承接");

        // 缩量回踩：缩量 + 价格下行（但非大跌）
        if (shrink && ind.ChangeRate < 0)
            return new(VolumeState.ShrinkPullback, false, "缩量回踩");

        // 缩量整理：缩量 + 价格平稳或小幅上涨
        if (shrink && ind.ChangeRate >= 0)
            return new(VolumeState.ShrinkConsolidate, false, "缩量整理");

        // 无量横盘：无量 + 无趋势
        if (!hasVolume && Math.Abs(ind.ChangeRate) < 1m)
            return new(VolumeState.NoVolumeSideways, false, "无量横盘");

        return new(VolumeState.VolumeDivergence, hasVolume, "资金分歧");
    }
}
