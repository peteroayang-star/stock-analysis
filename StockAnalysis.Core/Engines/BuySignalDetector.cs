using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>买入信号检测器，识别三种经典买点形态</summary>
public class BuySignalDetector
{
    private readonly IndicatorCalculator _calc = new();
    private readonly SignalConfig _cfg;

    /// <param name="cfg">信号检测参数配置</param>
    public BuySignalDetector(SignalConfig cfg) => _cfg = cfg;

    /// <summary>
    /// 检测指定 K 线位置是否出现买入信号
    /// </summary>
    /// <param name="bars">K 线列表</param>
    /// <param name="index">目标 K 线索引</param>
    /// <returns>信号类型和中文描述</returns>
    public (BuySignalType type, string reason) Detect(List<StockBar> bars, int index)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return (BuySignalType.None, "");

        var bar = bars[index];

        // 倍量突破：收盘突破近N日高点，且量能放大超过设定倍数
        if (bar.Close > ind.High20 && bar.Volume >= ind.VolMA5 * _cfg.VolumeMultiplier)
            return (BuySignalType.VolumeBreakout, $"收盘突破近{_cfg.BreakoutDays}日高点，量能放大{_cfg.VolumeMultiplier}倍");

        // 缩量回踩：多头排列下缩量回踩MA10，是加仓机会
        if (ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20
            && bar.Volume <= ind.VolMA5 * _cfg.ShrinkVolumeRatio
            && Math.Abs(bar.Close - ind.MA10) / ind.MA10 <= _cfg.PullbackNearMARatio)
            return (BuySignalType.PullbackSupport, $"多头排列缩量回踩MA10（±{_cfg.PullbackNearMARatio:P0}）");

        // 凹量洗盘：连续N日缩量后收盘守住MA20，洗盘结束信号
        if (index >= 19 + _cfg.WashoutDays
            && Enumerable.Range(1, _cfg.WashoutDays).All(n => bars[index - n + 1].Volume < bars[index - n].Volume)
            && bar.Close >= ind.MA20)
            return (BuySignalType.VolumeWashout, $"连续{_cfg.WashoutDays}日缩量洗盘，收盘守住MA20");

        return (BuySignalType.None, "");
    }
}
