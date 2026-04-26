using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>风险评分引擎，根据技术指标计算 0-100 的风险分</summary>
public class RiskScoreEngine
{
    private readonly IndicatorCalculator _calc = new();

    /// <summary>
    /// 计算指定 K 线的风险评分
    /// </summary>
    /// <param name="bars">K 线列表</param>
    /// <param name="index">目标 K 线索引</param>
    /// <returns>风险分（0-100）和中文原因列表</returns>
    public (int score, List<string> reasons) Score(List<StockBar> bars, int index)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return (50, ["数据不足"]);

        var bar = bars[index];
        int score = 0;
        var reasons = new List<string>();

        // 均线空头排列：MA5 < MA10 < MA20，趋势向下
        if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) { score += 30; reasons.Add("均线空头排列"); }
        // 上影线偏长：卖压较重
        if (ind.UpperShadowRatio > 0.4m)                { score += 20; reasons.Add($"上影线偏长({ind.UpperShadowRatio:P0})"); }
        // 放量下跌：主力出货信号
        if (ind.ChangeRate < -3m && bar.Volume >= ind.VolMA5 * 1.5m) { score += 25; reasons.Add("放量下跌"); }
        // 收盘跌破MA20：短期趋势转弱
        if (bar.Close < ind.MA20)                        { score += 15; reasons.Add("收盘跌破MA20"); }
        // 涨幅过大：追高风险
        if (ind.ChangeRate > 9m)                         { score += 10; reasons.Add("涨幅过大追高风险"); }

        return (Math.Min(score, 100), reasons);
    }
}
