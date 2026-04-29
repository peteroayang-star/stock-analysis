using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>风险评分引擎，依赖 VolumeResult，不再自行判断量价</summary>
public class RiskScoreEngine
{
    private readonly IndicatorCalculator _calc = new();

    public (int score, List<string> reasons) Score(
        List<StockBar> bars, int index,
        VolumeResult vol, bool marketWeak = false)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return (50, ["数据不足"]);

        var bar = bars[index];
        int score = 0;
        var reasons = new List<string>();

        if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) { score += 30; reasons.Add("均线空头排列"); }
        if (ind.UpperShadowRatio > 0.4m)                { score += 20; reasons.Add($"上影线偏长({ind.UpperShadowRatio:P0})"); }
        if (vol.State == VolumeState.VolumeDown)         { score += 25; reasons.Add("放量下跌"); }
        if (vol.State == VolumeState.VolumeStall)        { score += 15; reasons.Add("放量滞涨，主力派发嫌疑"); }
        if (bar.Close < ind.MA20)                        { score += 15; reasons.Add("收盘跌破MA20"); }
        if (ind.ChangeRate > 9m)                         { score += 10; reasons.Add("涨幅过大追高风险"); }
        if (ind.DIF < ind.DEA && ind.MACD < 0)          { score += 10; reasons.Add("MACD死叉"); }
        if (marketWeak)                                  { score += 20; reasons.Add("大盘弱势"); }

        if (score == 0) { score = 30; reasons.Add("无明显信号，建议观望"); }

        return (Math.Min(score, 100), reasons);
    }
}
