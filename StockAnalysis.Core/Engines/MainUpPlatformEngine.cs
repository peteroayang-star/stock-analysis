using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public record MainUpPlatformResult(
    bool IsMainUpPlatform,
    int PlatformDays,
    decimal PlatformHigh,
    decimal PlatformLow,
    decimal PlatformRangePercent,
    decimal RecentGain20,
    decimal VolumeShrinkRatio,
    decimal LockPositionStrength,
    decimal SecondWaveProbability,
    decimal BreakoutReadyScore,
    List<string> Tags,
    string Summary);

public class MainUpPlatformEngine
{
    private static MainUpPlatformResult Fail(string reason, List<string>? tags = null) =>
        new(false, 0, 0, 0, 0, 0, 1, 0, 0, 0, tags ?? [], reason);

    public MainUpPlatformResult Analyze(
        List<StockBar> bars, int index,
        IndicatorCalculator.Indicators? ind,
        VolumeResult vol, CycleResult cycle, int riskScore)
    {
        if (index < 28 || ind == null) return Fail("数据不足");

        var tags = new List<string>();

        // 危险排除
        if (vol.State is VolumeState.VolumeStall or VolumeState.VolumeDistribute)
            return Fail("放量滞涨或派发，非健康平台", ["放量滞涨"]);
        if (cycle.Cycle is MarketCycle.Distribute or MarketCycle.End)
            return Fail("周期处于派发或结束阶段", ["周期危险"]);
        if (ind.MA5 < ind.MA20)
            return Fail("价格跌破MA20，趋势已坏", ["跌破MA20"]);

        // 连续上影线检测
        var seg3 = bars.GetRange(index - 2, 3);
        int shadowCount = seg3.Count(b => {
            decimal r = b.High - b.Low;
            return r > 0 && (b.High - Math.Max(b.Open, b.Close)) / r > 0.45m;
        });
        if (shadowCount >= 2) return Fail("连续上影线，派发嫌疑", ["连续上影线"]);

        // MACD死叉且无修复结构
        bool macdDead = ind.DIF < ind.DEA;
        bool repairOk = ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20 && bars[index].Close >= ind.MA20;
        if (macdDead && !repairOk) return Fail("MACD死叉且无修复结构", ["MACD死叉"]);

        // 前置主升条件
        decimal gain20 = bars[index - 19].Close > 0
            ? (bars[index].Close - bars[index - 19].Close) / bars[index - 19].Close : 0;
        bool hasLimitUp = false;
        for (int n = 1; n <= 19; n++)
        {
            if (bars[index - n + 1].Close > 0 && bars[index - n - 1].Close > 0 &&
                (bars[index - n].High - bars[index - n - 1].Close) / bars[index - n - 1].Close >= 0.095m)
            { hasLimitUp = true; break; }
        }
        if (!(gain20 >= 0.25m || hasLimitUp) || ind.MA5 <= ind.MA10 || ind.MA10 <= ind.MA20)
            return Fail("未形成主升浪基础");

        // 平台窗口检测（从10到3，取最长满足条件的）
        decimal priorSurgeAvgVol = index >= 14
            ? (decimal)bars.GetRange(index - 14, 5).Average(b => b.Volume) : 0;
        if (priorSurgeAvgVol <= 0) return Fail("数据不足");

        int platformWindow = 0;
        decimal platformHi = 0, platformLo = 0;
        for (int w = 10; w >= 3; w--)
        {
            if (index - w + 1 < 0) continue;
            var seg = bars.GetRange(index - w + 1, w);
            decimal hi = seg.Max(b => b.High);
            decimal lo = seg.Min(b => b.Low);
            if (lo <= 0) continue;
            decimal rangeRatio = (hi - lo) / lo;
            bool noCloseBelowMA10 = seg.All(b => b.Close >= ind.MA10 * 0.995m);
            bool noLowBreak = seg.All(b => b.Low >= lo * 0.97m);
            int shrinkDays = seg.Count(b => (decimal)b.Volume < priorSurgeAvgVol);
            decimal avgVol = (decimal)seg.Average(b => b.Volume);
            if (rangeRatio <= 0.12m && noCloseBelowMA10 && noLowBreak
                && shrinkDays >= 2 && avgVol < priorSurgeAvgVol)
            { platformWindow = w; platformHi = hi; platformLo = lo; break; }
        }
        if (platformWindow == 0) return Fail("未形成有效横盘平台");

        var platSeg = bars.GetRange(index - platformWindow + 1, platformWindow);
        decimal platRange = platformLo > 0 ? (platformHi - platformLo) / platformLo : 0;
        decimal shrinkRatio = priorSurgeAvgVol > 0
            ? (decimal)platSeg.Average(b => b.Volume) / priorSurgeAvgVol : 1;

        // LockPositionStrength 评分
        decimal lockScore = 0;
        var upBars   = platSeg.Where(b => b.Close >= b.Open).ToList();
        var downBars = platSeg.Where(b => b.Close < b.Open).ToList();
        decimal avgUpVol   = upBars.Count   > 0 ? (decimal)upBars.Average(b => b.Volume)   : 0;
        decimal avgDownVol = downBars.Count > 0 ? (decimal)downBars.Average(b => b.Volume) : 0;
        if (avgDownVol < avgUpVol && avgUpVol > 0) { lockScore += 20; tags.Add("缩量下跌"); }

        if (platSeg.Any(b => b.Low < ind.MA5 && b.Close >= ind.MA5))  { lockScore += 15; tags.Add("回踩MA5收回"); }
        if (platSeg.Any(b => b.Low < ind.MA10 && b.Close >= ind.MA10)) { lockScore += 15; tags.Add("回踩MA10收回"); }

        bool risingLows = platSeg.Count >= 2;
        for (int i = 1; i < platSeg.Count && risingLows; i++)
            if (platSeg[i].Low <= platSeg[i - 1].Low) risingLows = false;
        if (risingLows) { lockScore += 15; tags.Add("低点抬高"); }

        bool noConsecNeg = true;
        int negRun = 0;
        foreach (var b in platSeg)
        {
            decimal body = b.Open > 0 ? Math.Abs(b.Close - b.Open) / b.Open : 0;
            if (b.Close < b.Open && body > 0.03m) negRun++;
            else negRun = 0;
            if (negRun >= 2) { noConsecNeg = false; break; }
        }
        if (noConsecNeg) { lockScore += 15; tags.Add("无连续大阴"); }
        if (riskScore <= 50) lockScore += 10;

        bool ampShrink = downBars.Count >= 2 &&
            downBars.Zip(downBars.Skip(1), (a, b) => (a.High - a.Low) > (b.High - b.Low)).All(x => x);
        if (ampShrink) { lockScore += 10; tags.Add("振幅收缩"); }

        lockScore = Math.Clamp(lockScore, 0, 100);

        // BreakoutReadyScore
        decimal brkScore = 0;
        if (lockScore >= 70) brkScore += 30;
        if (shrinkRatio < 0.7m) brkScore += 20;
        if (platRange <= 0.06m) brkScore += 20;
        if (gain20 >= 0.40m) brkScore += 15;
        if (cycle.Cycle is MarketCycle.Diverge or MarketCycle.Consensus) brkScore += 15;
        brkScore = Math.Clamp(brkScore, 0, 100);

        decimal secondWave = Math.Clamp(lockScore * 0.5m + brkScore * 0.5m, 0, 100);

        string summary = lockScore >= 70 && secondWave >= 65
            ? $"主升平台锁仓强，横盘{platformWindow}天，二波概率{secondWave:F0}%，可等待突破信号"
            : $"主升后横盘{platformWindow}天，锁仓强度{lockScore:F0}，二波概率{secondWave:F0}%，继续观察";

        return new MainUpPlatformResult(
            true, platformWindow, platformHi, platformLo, platRange,
            gain20, shrinkRatio, lockScore, secondWave, brkScore, tags, summary);
    }
}
