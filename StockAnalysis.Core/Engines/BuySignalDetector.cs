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

        // 倍量突破：收盘突破近N日高点，且量能放大超过设定倍数，次日不回落确认
        if (bar.Close > ind.High20 && bar.Volume >= ind.VolMA5 * _cfg.VolumeMultiplier)
        {
            // 突破确认：收盘 > 开盘（阳线），避免假突破
            if (bar.Close > bar.Open)
                return (BuySignalType.VolumeBreakout, $"收盘突破近{_cfg.BreakoutDays}日高点，量能放大{_cfg.VolumeMultiplier}倍（阳线确认）");
        }

        // MACD金叉：DIF上穿DEA，且MACD柱由负转正
        var prevInd = _calc.Calculate(bars, index - 1);
        if (prevInd != null && ind.DIF > ind.DEA && prevInd.DIF <= prevInd.DEA && ind.MACD > 0)
            return (BuySignalType.PullbackSupport, "MACD金叉，DIF上穿DEA");

        // 缩量回踩：多头排列下缩量回踩MA10，收盘>开盘（下影线承接）
        if (ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20
            && bar.Volume <= ind.VolMA5 * _cfg.ShrinkVolumeRatio
            && Math.Abs(bar.Close - ind.MA10) / ind.MA10 <= _cfg.PullbackNearMARatio
            && bar.Close >= bar.Open)
            return (BuySignalType.PullbackSupport, $"多头排列缩量回踩MA10（±{_cfg.PullbackNearMARatio:P0}），下影线承接");

        // 凹量洗盘：连续N日（不含当日）缩量后收盘守住MA20
        if (index >= 19 + _cfg.WashoutDays
            && Enumerable.Range(1, _cfg.WashoutDays).All(n => bars[index - n].Volume < bars[index - n - 1].Volume)
            && bar.Close >= ind.MA20)
            return (BuySignalType.VolumeWashout, $"连续{_cfg.WashoutDays}日缩量洗盘，收盘守住MA20");

        return (BuySignalType.None, "");
    }
}
