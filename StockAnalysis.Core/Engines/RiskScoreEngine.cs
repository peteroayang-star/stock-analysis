using StockAnalysis.Core.Models;
namespace StockAnalysis.Core.Engines;

public record RiskResult(int TrendRisk, int VolatilityRisk, int SentimentRisk, int Total, List<string> Reasons);

public class RiskScoreEngine
{
    private readonly IndicatorCalculator _calc = new();

    public RiskResult Score(List<StockBar> bars, int index, VolumeResult vol, bool marketWeak = false,
        StockStyle style = StockStyle.TrendInstitutional, PositionResult? position = null)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return new(50, 0, 0, 50, ["数据不足"]);
        var bar = bars[index];
        var reasons = new List<string>();

        // ── 趋势风险：均线空头、跌破MA20、MACD死叉 ──────
        int trendRisk = 0;
        if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) { trendRisk += 50; reasons.Add("均线空头排列"); }
        if (bar.Close < ind.MA20)                       { trendRisk += 30; reasons.Add("收盘跌破MA20"); }
        if (ind.DIF < ind.DEA && ind.MACD < 0)         { trendRisk += 20; reasons.Add("MACD死叉"); }
        if (marketWeak)                                 { trendRisk += 20; reasons.Add("大盘弱势"); }
        trendRisk = Math.Min(trendRisk, 100);

        // ── 波动风险：上影线、放量下跌、放量滞涨、派发 ──
        int volRisk = 0;
        var low20Close = bars.Skip(Math.Max(0, index - 19)).Take(Math.Min(20, index + 1)).Min(b => b.Close);
        var gainPct = low20Close > 0 ? (double)((bar.Close - low20Close) / low20Close * 100) : 0;
        if (gainPct > 15 && ind.UpperShadowRatio > 0.3m) { volRisk += 40; reasons.Add("高位上影线，派发嫌疑"); }
        else if (ind.UpperShadowRatio > 0.4m)             { volRisk += 20; reasons.Add("上影线偏长"); }
        if (vol.State == VolumeState.WeakSupport)         { volRisk += 40; reasons.Add("放量下跌"); }
        if (vol.State == VolumeState.VolumeStall)         { volRisk += 25; reasons.Add("放量滞涨"); }
        if (vol.State == VolumeState.VolumeDistribute)    { volRisk += 40; reasons.Add("放量派发"); }
        volRisk = Math.Min(volRisk, 100);

        // ── 情绪/过热风险：追高、偏离均线、连续涨停、高换手 ──
        int sentRisk = 0;
        if (ind.ChangeRate > 9m)                              { sentRisk += 20; reasons.Add("涨幅过大追高风险"); }
        if (ind.MA20 > 0 && bar.Close > ind.MA20 * 1.15m)    { sentRisk += 30; reasons.Add("价格严重偏离均线，追高风险"); }

        int limitUpCount = 0;
        for (int i = Math.Max(1, index - 4); i <= index; i++)
            if (bars[i - 1].Close > 0 && (bars[i].High - bars[i - 1].Close) / bars[i - 1].Close >= 0.095m)
                limitUpCount++;
        if (limitUpCount >= 3) { sentRisk += 30; reasons.Add($"近5日{limitUpCount}次涨停，情绪高潮"); }

        // ── 新增过热线风险因子 (PositionEngine 提供位置信息) ──
        if (position != null)
        {
            // 连续阳线 → 过热风险
            if (position.IsConsecutiveYang && position.Level == PositionLevel.High)
            { sentRisk += 15; reasons.Add("连续阳线，短线过热"); }

            // 偏离MA5超过12% → 极度过热
            if (position.DistFromMA5 > 12m)
            { sentRisk += 20; reasons.Add($"偏离MA5 {position.DistFromMA5:F0}%，追高风险极大"); }

            // 高位 + 爆量加速 → 危险
            if (position.IsVolumeExplosive && position.Level == PositionLevel.High)
            { sentRisk += 25; reasons.Add("高位爆量加速，疑似诱多"); }

            // 疲劳上涨（缩量高位） → 动能衰竭
            if (position.Acceleration == AccelerationState.Fatigued && position.Level == PositionLevel.High)
            { sentRisk += 20; reasons.Add("高位疲劳上涨，动能衰竭"); }

            // 高位 + 偏离MA20过远
            if (position.Level == PositionLevel.High && position.DistFromMA20 > 25m)
            { sentRisk += 20; reasons.Add($"偏离MA20 {position.DistFromMA20:F0}%，严重高估"); }
        }

        sentRisk = Math.Min(sentRisk, 100);

        // ── 综合风险 ──────────────────────────
        int total = style switch
        {
            StockStyle.TrendInstitutional or StockStyle.LargeCapVolume
                => (int)(trendRisk * 0.60 + volRisk * 0.30 + sentRisk * 0.10),
            StockStyle.EmotionSpeculative
                => (int)(trendRisk * 0.40 + volRisk * 0.25 + sentRisk * 0.35),
            StockStyle.DistributionDecline
                => (int)(trendRisk * 0.40 + volRisk * 0.45 + sentRisk * 0.15),
            _ => (int)(trendRisk * 0.50 + volRisk * 0.30 + sentRisk * 0.20)
        };

        // ── 风险地板：过热状态下风险不得低于阈值 ──
        // 换手>15% 且 偏离MA5>12% → 风险不低于55
        if (position != null && bar.Amount > 0)
        {
            var recent20AvgVol = bars.Skip(Math.Max(0, index - 19)).Take(20).Average(b => (double)b.Volume);
            var turnoverEstimate = recent20AvgVol > 0 ? (double)bar.Volume / recent20AvgVol : 1;
            var isHighTurnover = turnoverEstimate > 3.0; // 换手率代理：量比>3

            if (isHighTurnover && position.DistFromMA5 > 12m && total < 55)
            { total = 55; reasons.Add("高换手+大幅偏离均线，风险下限55"); }

            if (position.IsVolumeExplosive && position.Level == PositionLevel.High && total < 50)
            { total = 50; reasons.Add("高位爆量，风险不低于50"); }

            if (position.Acceleration == AccelerationState.ExplosiveVolume && total < 45)
            { total = 45; reasons.Add("爆量加速，风险不低于45"); }
        }

        if (total == 0) { total = 30; reasons.Add("无明显风险信号"); }

        return new(trendRisk, volRisk, sentRisk, Math.Min(total, 100), reasons);
    }
}
