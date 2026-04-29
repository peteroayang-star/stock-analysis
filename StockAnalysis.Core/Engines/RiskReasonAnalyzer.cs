using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>风险原因分析器，生成中文风险描述和操作建议</summary>
public class RiskReasonAnalyzer
{
    private readonly IndicatorCalculator _calc = new();

    /// <summary>
    /// 分析指定 K 线的风险原因，生成中文描述列表
    /// </summary>
    /// <param name="bars">K 线列表</param>
    /// <param name="index">目标 K 线索引</param>
    /// <returns>中文风险原因列表</returns>
    public List<string> Analyze(List<StockBar> bars, int index)
    {
        var reasons = new List<string>();
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return reasons;

        var bar = bars[index];
        if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) reasons.Add("均线空头排列");
        if (ind.UpperShadowRatio > 0.4m) reasons.Add($"上影线偏长({ind.UpperShadowRatio:P0})");
        if (ind.ChangeRate < -3m && bar.Volume >= ind.VolMA5 * 1.5m) reasons.Add("放量下跌");
        if (bar.Close < ind.MA20) reasons.Add("收盘跌破MA20");
        if (ind.ChangeRate > 9m) reasons.Add("涨幅过大，追高风险");
        if (bar.Amount < 5_000_000m) reasons.Add("成交额不足");

        return reasons;
    }

    /// <summary>
    /// 根据信号和决策生成一句话中文操作建议
    /// </summary>
    /// <param name="signal">买入信号类型</param>
    /// <param name="decision">交易决策</param>
    /// <param name="reasons">风险原因列表</param>
    /// <returns>中文操作建议</returns>
    public string Suggestion(BuySignalType signal, Decision decision, List<string> reasons)
    {
        if (decision == Decision.Buy)
            return $"{SignalName(signal)}，风险可控，可轻仓介入，注意量能配合";
        if (decision == Decision.TryBuy)
            return "价格突破观察位，趋势向上且风险较低，可小仓试错，存在假突破风险需严格止损";
        if (decision == Decision.Watch)
            return $"{SignalName(signal)}，但存在{string.Join("、", reasons)}，观察次日是否站稳";
        if (decision == Decision.Sell)
            return "风险过高，建议止损离场";
        if (decision == Decision.Reduce)
            return "风险偏高，建议减仓至半仓以下";
        if (decision == Decision.Hold)
            return "趋势健康，持有等待信号";
        return "无明显信号，暂时观望";
    }

    /// <summary>将信号类型枚举转换为中文名称</summary>
    private static string SignalName(BuySignalType t) => t switch
    {
        BuySignalType.VolumeBreakout => "倍量突破",
        BuySignalType.PullbackSupport => "缩量回踩",
        BuySignalType.VolumeWashout => "凹量洗盘",
        _ => "无信号"
    };
}
