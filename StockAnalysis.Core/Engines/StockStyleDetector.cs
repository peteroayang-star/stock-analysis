using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>票型识别引擎：先识别股票风格，再决定评分标准</summary>
public class StockStyleDetector
{
    public StockStyle Detect(List<StockBar> bars, int index, int limitUpCount14)
    {
        if (index < 20) return StockStyle.TrendInstitutional;

        var bar    = bars[index];
        var recent = bars.Skip(index - 19).Take(20).ToList();

        decimal ma5  = recent.TakeLast(5).Average(b => b.Close);
        decimal ma10 = recent.TakeLast(10).Average(b => b.Close);
        decimal ma20 = recent.Average(b => b.Close);
        decimal volMa10 = (decimal)recent.TakeLast(10).Average(b => b.Volume);

        // 振幅均值（近10日）
        decimal avgAmp = recent.TakeLast(10).Average(b =>
            b.Close > 0 ? (b.High - b.Low) / b.Close : 0);

        // 近20日涨停次数
        int limitUp20 = 0;
        for (int i = index - 19; i <= index; i++)
            if (bars[i - 1].Close > 0 && (bars[i].High - bars[i - 1].Close) / bars[i - 1].Close >= 0.095m)
                limitUp20++;

        // 出货信号数
        decimal range = bar.High - bar.Low;
        decimal upperShadow = range > 0 ? (bar.High - Math.Max(bar.Open, bar.Close)) / range : 0;
        bool volStall    = bar.Volume > volMa10 * 1.5m && Math.Abs(bar.Close - bar.Open) / bar.Open < 0.01m;
        bool bigNegBreak = bar.Close < bar.Open && (bar.Open - bar.Close) / bar.Open > 0.03m
                        && bar.Volume > volMa10 * 1.5m && bar.Close < ma10;
        bool breakPlatform = index >= 3
            && bars.Skip(index - 2).Take(3).All(b => b.Close < b.Open)
            && bar.Close < ma20 * 0.97m;
        int distSignals = (volStall ? 1 : 0) + (bigNegBreak ? 1 : 0) + (breakPlatform ? 1 : 0)
                        + (upperShadow > 0.5m ? 1 : 0);

        // ── 出货衰退型 ────────────────────────────────────────
        if (distSignals >= 2 || (breakPlatform && bigNegBreak))
            return StockStyle.DistributionDecline;

        // ── 妖股情绪型：高频涨停 + 高振幅 ────────────────────
        if (limitUpCount14 >= 2 || (limitUp20 >= 2 && avgAmp > 0.06m))
            return StockStyle.EmotionSpeculative;

        // ── 低位试盘型：底部放量异动 + 首次突破 ──────────────
        bool nearBottom = bar.Close < ma20 * 1.05m && bar.Close > ma20 * 0.90m;
        bool volumeSpike = bar.Volume > volMa10 * 2m;
        bool firstBreak  = limitUp20 <= 1 && bar.Close > recent.SkipLast(1).Max(b => b.High);
        if (nearBottom && volumeSpike && firstBreak)
            return StockStyle.BottomLaunch;

        // ── 趋势机构型：MA多头排列 + 台阶推进 ────────────────
        bool trendUp   = ma5 > ma10 && ma10 > ma20;
        // 台阶式上涨：近20日低点持续抬高（至少60%的日子低点高于前日）
        int risingLowDays = 0;
        for (int i = index - 18; i <= index; i++)
            if (bars[i].Low > bars[i - 1].Low) risingLowDays++;
        bool stairStep = risingLowDays >= 11; // 超过55%的日子低点抬高

        // 回踩不破：近10日最低点不低于MA20的97%
        bool pullbackHolds = recent.TakeLast(10).Min(b => b.Low) >= ma20 * 0.97m;

        bool volSmooth = (decimal)recent.TakeLast(5).Average(b => b.Volume)
                       < (decimal)recent.Take(10).Average(b => b.Volume) * 1.5m; // 量能平稳（放宽）

        if (trendUp && (stairStep || pullbackHolds) && volSmooth && limitUp20 <= 1)
            return StockStyle.TrendInstitutional;

        // ── 中军容量型：趋势向上但振幅较大、量能稳定 ─────────
        if (trendUp)
            return StockStyle.LargeCapVolume;

        return StockStyle.TrendInstitutional;
    }
}
