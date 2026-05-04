using StockAnalysis.Core.Models;
namespace StockAnalysis.Core.Engines;

public record SmartMoneyResult(SmartMoneyBehavior Behavior, string Description);

public class SmartMoneyEngine
{
    private readonly IndicatorCalculator _calc = new();

    public SmartMoneyResult Analyze(List<StockBar> bars, int index, VolumeResult vol)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return new(SmartMoneyBehavior.None, "无主力行为");
        var bar = bars[index];
        bool highPos = bar.Close > ind.MA20 * 1.15m;
        bool aboveMA20 = bar.Close >= ind.MA20;
        bool highShadow = ind.UpperShadowRatio > 0.4m;
        bool bigVol = (decimal)bar.Volume >= ind.VolMA5 * 1.5m;
        bool shrink = (decimal)bar.Volume < ind.VolMA5 * 0.8m;
        bool isUp = ind.ChangeRate > 0;
        bool isDown = ind.ChangeRate < -0.5m;

        // 出货：放量下跌 + 跌破关键位
        if (bigVol && isDown && !aboveMA20)
            return new(SmartMoneyBehavior.Dumping, "出货");

        // 派发：高位放量滞涨/长上影
        if (highPos && bigVol && (highShadow || Math.Abs(ind.ChangeRate) < 2m))
            return new(SmartMoneyBehavior.Distribution, "派发");

        // 高位震荡
        if (highPos && !bigVol && Math.Abs(ind.ChangeRate) < 2m)
            return new(SmartMoneyBehavior.HighShock, "高位震荡");

        // 主动进攻：放量上涨 + 收盘接近最高价
        if (bigVol && isUp && ind.UpperShadowRatio < 0.3m)
            return new(SmartMoneyBehavior.AggressiveAttack, "主动进攻");

        // 分歧换手：高换手 + 冲高回落 + 未破趋势
        if (bigVol && highShadow && aboveMA20)
            return new(SmartMoneyBehavior.DivergenceSwap, "分歧换手");

        // 洗盘：缩量回踩 + 下影明显 + 未跌破MA20
        var lowerShadow = bar.High > bar.Low
            ? (Math.Min(bar.Open, bar.Close) - bar.Low) / (bar.High - bar.Low)
            : 0m;
        if (shrink && isDown && aboveMA20 && lowerShadow > 0.3m)
            return new(SmartMoneyBehavior.Washout, "洗盘");

        // 吸筹：缩量横盘 + 重心缓慢抬高
        if (shrink && !isDown && aboveMA20)
        {
            var prevInd = index >= 1 ? _calc.Calculate(bars, index - 1) : null;
            if (prevInd != null && ind.MA20 >= prevInd.MA20)
                return new(SmartMoneyBehavior.Accumulation, "吸筹");
        }

        return new(SmartMoneyBehavior.None, "无主力行为");
    }
}
