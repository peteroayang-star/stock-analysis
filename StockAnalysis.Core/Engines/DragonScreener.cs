using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>龙头回调选股：近期涨停 + 调整 + 十字星信号</summary>
public class DragonScreener
{
    private readonly IndicatorCalculator _calc = new();

    public record DragonResult(string Code, string Name, DateTime LimitUpDate, decimal LimitUpClose, string DojiType, decimal VolRatio);

    /// <param name="bars">日线数据（至少30根）</param>
    /// <param name="lookbackDays">向前查找涨停的天数</param>
    public DragonResult? Screen(List<StockBar> bars, int lookbackDays = 15)
    {
        if (bars.Count < 30) return null;
        int last = bars.Count - 1;
        var today = bars[last];

        // 1. 近N日内有涨停（涨幅≥9.5%）
        int limitUpIdx = -1;
        for (int i = last - 1; i >= Math.Max(0, last - lookbackDays); i--)
        {
            var b = bars[i];
            if (b.Open <= 0) continue;
            var chg = (b.Close - bars[i - 1].Close) / bars[i - 1].Close * 100;
            if (chg >= 9.5m) { limitUpIdx = i; break; }
        }
        if (limitUpIdx < 0) return null;

        // 2. 涨停后至少调整2日
        int daysSinceLimitUp = last - limitUpIdx;
        if (daysSinceLimitUp < 2) return null;

        // 3. 当前收盘低于涨停日收盘（有回调）
        if (today.Close >= bars[limitUpIdx].Close) return null;

        // 4. 过滤退潮下跌趋势：均线空头排列（MA5 < MA10 < MA20）直接排除
        var ind = _calc.Calculate(bars, last);
        if (ind == null) return null;
        if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) return null;

        // 5. 十字星判断（最近1日）

        decimal range = today.High - today.Low;
        if (range <= 0) return null;
        decimal bodyRatio = Math.Abs(today.Close - today.Open) / range;
        if (bodyRatio > 0.15m) return null; // 实体>15%不是十字星

        // 排除墓碑十字（上影线极长，下影线极短）
        decimal upperShadow = today.High - Math.Max(today.Open, today.Close);
        decimal lowerShadow = Math.Min(today.Open, today.Close) - today.Low;
        if (upperShadow > range * 0.7m && lowerShadow < range * 0.1m) return null;

        string dojiType = lowerShadow > range * 0.5m ? "蜻蜓十字" : "十字星";

        // 5. 量能判断
        decimal volRatio = ind.VolMA5 > 0 ? (decimal)today.Volume / ind.VolMA5 : 0;

        return new DragonResult(today.Code, today.Name, bars[limitUpIdx].Date, bars[limitUpIdx].Close, dojiType, Math.Round(volRatio, 2));
    }
}
