using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public enum VolumeState
{
    VolumeUp,       // 放量上涨
    ShrinkUp,       // 缩量上涨
    VolumeDown,     // 放量下跌
    VolumeStall,    // 放量滞涨（量大价不动）
    NoVolume,       // 无量无趋势
    Normal          // 正常
}

public record VolumeResult(
    VolumeState State,
    bool HasEffectiveVolume,  // 是否存在有效放量（量 > 5日均量）
    string Description
);

public class VolumeEngine
{
    private readonly IndicatorCalculator _calc = new();

    public VolumeResult Analyze(List<StockBar> bars, int index)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return new(VolumeState.Normal, false, "");

        var bar = bars[index];
        var vol = (decimal)bar.Volume;
        bool isUp = ind.ChangeRate > 0;
        bool isDown = ind.ChangeRate < -0.5m;
        bool hasVolume = vol > ind.VolMA5;
        bool shrink = vol < ind.VolMA5 * 0.8m;
        bool stall = hasVolume && Math.Abs(ind.ChangeRate) < 1m;

        if (hasVolume && isUp)   return new(VolumeState.VolumeUp,   true,  "放量上涨");
        if (hasVolume && isDown) return new(VolumeState.VolumeDown,  true,  "放量下跌");
        if (stall)               return new(VolumeState.VolumeStall, true,  "放量滞涨");
        if (shrink && isUp && ind.ChangeRate < 2m)
                                 return new(VolumeState.ShrinkUp,    false, "缩量上涨");
        if (!hasVolume && Math.Abs(ind.ChangeRate) < 1m)
                                 return new(VolumeState.NoVolume,    false, "无量无趋势");

        return new(VolumeState.Normal, hasVolume, "");
    }
}
