using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>信号稳定性过滤器，评估趋势一致性、波动率和反向波动约束</summary>
public class StockStabilityFilter
{
    private readonly IndicatorCalculator _calc = new();

    /// <summary>
    /// 计算稳定性评分（0-100）并判断是否通过
    /// </summary>
    /// <returns>(score, passRequired) — score为评分，passRequired表示必要条件是否全部满足</returns>
    public (int score, bool passRequired) Evaluate(List<StockBar> bars, int index)
    {
        if (index < 28) return (0, false);

        var ind0 = _calc.Calculate(bars, index);
        var ind1 = _calc.Calculate(bars, index - 1);
        var ind2 = _calc.Calculate(bars, index - 2);
        if (ind0 == null || ind1 == null || ind2 == null) return (0, false);

        // ── 一、趋势一致性（必须）──────────────────────────────
        bool ma5Rising3   = ind0.MA5 > ind1.MA5 && ind1.MA5 > ind2.MA5;
        bool bullishAlign = ind0.MA5 > ind0.MA10 && ind0.MA10 > ind0.MA20;
        bool ma10Rising3  = ind0.MA10 > ind1.MA10 && ind1.MA10 > ind2.MA10;
        bool trendOk = ma5Rising3 && bullishAlign && ma10Rising3;

        // ── 二、波动率过滤（必须）──────────────────────────────
        decimal Amplitude(StockBar b, decimal prevClose) =>
            prevClose == 0 ? 0 : (b.High - b.Low) / prevClose * 100;

        var amp5 = Enumerable.Range(1, 5)
            .Select(n => Amplitude(bars[index - n + 1], bars[index - n].Close))
            .Average();
        var amp3 = Enumerable.Range(1, 3)
            .Select(n => Amplitude(bars[index - n + 1], bars[index - n].Close))
            .Average();
        bool volOk = amp5 < 8m && amp3 < 6m;

        bool passRequired = trendOk && volOk;

        // ── 三、反向波动约束（轻量）────────────────────────────
        bool noLargeDrop = true;
        bool noHeavySell = true;
        for (int n = 1; n <= 2; n++)
        {
            var b = bars[index - n + 1];
            var pc = bars[index - n].Close;
            if (pc == 0) continue;
            decimal chg = (b.Close - pc) / pc * 100;
            if (chg < -5m) noLargeDrop = false;
            var volMa5 = (decimal)bars.Skip(index - n - 4).Take(5).Average(x => x.Volume);
            if (chg < -3m && b.Volume >= volMa5 * 1.5m) noHeavySell = false;
        }

        // ── 四、评分 ───────────────────────────────────────────
        int score = 0;
        if (trendOk)    score += 40;
        if (volOk)      score += 30;
        if (noHeavySell) score += 20;
        if (noLargeDrop) score += 10;

        return (score, passRequired);
    }
}
