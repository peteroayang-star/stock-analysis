using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>技术指标计算器，计算均线、量能、涨幅、上影线等指标</summary>
public class IndicatorCalculator
{
    /// <summary>计算结果记录</summary>
    /// <param name="MA5">5日收盘均价</param>
    /// <param name="MA10">10日收盘均价</param>
    /// <param name="MA20">20日收盘均价</param>
    /// <param name="VolMA5">5日成交量均值</param>
    /// <param name="VolMA10">10日成交量均值</param>
    /// <param name="High20">近20日最高价</param>
    /// <param name="ChangeRate">当日涨跌幅（%）</param>
    /// <param name="UpperShadowRatio">上影线比例 = (High - Max(Open,Close)) / (High - Low)</param>
    public record Indicators(
        decimal MA5, decimal MA10, decimal MA20,
        decimal VolMA5, decimal VolMA10,
        decimal High20,
        decimal ChangeRate,
        decimal UpperShadowRatio
    );

    /// <summary>
    /// 计算指定 K 线位置的技术指标
    /// </summary>
    /// <param name="bars">K 线列表（按日期升序）</param>
    /// <param name="index">目标 K 线索引，至少需要 index >= 19</param>
    /// <returns>指标对象，数据不足时返回 null</returns>
    public Indicators? Calculate(List<StockBar> bars, int index)
    {
        if (index < 19) return null;

        var bar = bars[index];
        decimal ma(int n) => bars.Skip(index - n + 1).Take(n).Average(b => b.Close);
        decimal volMa(int n) => (decimal)bars.Skip(index - n + 1).Take(n).Average(b => b.Volume);

        var high20 = bars.Skip(index - 19).Take(20).Max(b => b.High);
        var prevClose = bars[index - 1].Close;
        var changeRate = prevClose == 0 ? 0 : (bar.Close - prevClose) / prevClose * 100;
        var range = bar.High - bar.Low;
        var upperShadow = range == 0 ? 0 : (bar.High - Math.Max(bar.Open, bar.Close)) / range;

        return new Indicators(ma(5), ma(10), ma(20), volMa(5), volMa(10),
            high20, changeRate, upperShadow);
    }
}
