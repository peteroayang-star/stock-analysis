using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public record ChipControlResult(
    decimal SupportStrength,
    decimal ChipLockScore,
    decimal DownsideResistance,
    bool IsStrongSupport,
    bool IsPlatformLocked,
    bool IsDistributionSuspected,
    List<string> Tags,
    string Summary);

public class ChipControlEngine
{
    public ChipControlResult Analyze(List<StockBar> bars, int index, IndicatorCalculator.Indicators? ind, VolumeResult vol)
    {
        if (index < 10 || ind == null)
            return new ChipControlResult(50, 50, 50, false, false, false, [], "数据不足");

        var tags = new List<string>();
        decimal score = 0;

        var seg10 = bars.GetRange(Math.Max(0, index - 9), Math.Min(10, index + 1));
        var seg5  = bars.GetRange(Math.Max(0, index - 4), Math.Min(5, index + 1));

        // 加分：下跌日量 < 上涨日量
        var upBars   = seg10.Where(b => b.Close >= b.Open).ToList();
        var downBars = seg10.Where(b => b.Close < b.Open).ToList();
        decimal avgUpVol   = upBars.Count   > 0 ? (decimal)upBars.Average(b => b.Volume)   : 0;
        decimal avgDownVol = downBars.Count > 0 ? (decimal)downBars.Average(b => b.Volume) : 0;
        if (avgDownVol < avgUpVol && avgUpVol > 0) { score += 20; tags.Add("缩量下跌"); }

        // 加分：收盘回到 MA5 上方（近5根有低于MA5后收回）
        bool recoveredMA5 = seg5.Any(b => b.Low < ind.MA5 && b.Close >= ind.MA5);
        if (recoveredMA5) { score += 15; tags.Add("回踩MA5收回"); }

        // 加分：收盘回到 MA10 上方
        bool recoveredMA10 = seg5.Any(b => b.Low < ind.MA10 && b.Close >= ind.MA10);
        if (recoveredMA10) { score += 15; tags.Add("回踩MA10收回"); }

        // 加分：最近5根低点逐步抬高
        bool risingLows = true;
        for (int i = 1; i < seg5.Count; i++)
            if (seg5[i].Low <= seg5[i - 1].Low) { risingLows = false; break; }
        if (risingLows && seg5.Count >= 3) { score += 15; tags.Add("低点抬高"); }

        // 加分：上涨日总量 > 下跌日总量
        decimal totalUpVol   = upBars.Sum(b => (decimal)b.Volume);
        decimal totalDownVol = downBars.Sum(b => (decimal)b.Volume);
        if (totalUpVol > totalDownVol) { score += 15; tags.Add("量能偏多"); }

        // 加分：尾盘修复（收盘在当日振幅上30%位置）
        var cur = bars[index];
        decimal range = cur.High - cur.Low;
        if (range > 0 && (cur.Close - cur.Low) / range >= 0.7m) { score += 10; tags.Add("尾盘修复"); }

        // 加分：无连续大阴
        bool noConsecNeg = true;
        int negCount = 0;
        foreach (var b in seg5)
        {
            decimal body = b.Open > 0 ? Math.Abs(b.Close - b.Open) / b.Open : 0;
            if (b.Close < b.Open && body > 0.03m) negCount++;
            else negCount = 0;
            if (negCount >= 2) { noConsecNeg = false; break; }
        }
        if (noConsecNeg) { score += 10; tags.Add("无连续大阴"); }

        // 扣分
        if (vol.State == VolumeState.VolumeStall)       { score -= 20; tags.Add("放量滞涨"); }
        if (vol.State == VolumeState.VolumeDistribute)  { score -= 20; tags.Add("放量派发"); }

        var seg3 = bars.GetRange(Math.Max(0, index - 2), Math.Min(3, index + 1));
        int shadowCount = seg3.Count(b => {
            decimal r = b.High - b.Low;
            return r > 0 && (b.High - Math.Max(b.Open, b.Close)) / r > 0.45m;
        });
        if (shadowCount >= 2) { score -= 15; tags.Add("连续上影线"); }

        bool hasBigNeg = seg5.Any(b => {
            decimal body = b.Open > 0 ? Math.Abs(b.Close - b.Open) / b.Open : 0;
            return b.Close < b.Open && body > 0.03m && (decimal)b.Volume > ind.VolMA10;
        });
        if (hasBigNeg) { score -= 20; tags.Add("放量大阴"); }

        decimal platformLow = seg10.Min(b => b.Low);
        if (cur.Close < platformLow) { score -= 25; tags.Add("跌破平台"); }

        if (ind.MA5 < ind.MA20) { score -= 20; tags.Add("跌破MA20"); }

        decimal chipLock = Math.Clamp(score, 0, 100);
        decimal support  = Math.Clamp(chipLock * 0.6m + (vol.State == VolumeState.ShrinkConsolidate ? 20 : 0) + (ind.MA5 > ind.MA10 ? 20 : 0), 0, 100);
        decimal downRes  = Math.Clamp(support * 0.7m + chipLock * 0.3m, 0, 100);

        bool isStrong  = support >= 70 && chipLock >= 60;
        bool isLocked  = chipLock >= 70 && vol.State == VolumeState.ShrinkConsolidate;
        bool isDist    = vol.State is VolumeState.VolumeStall or VolumeState.VolumeDistribute || chipLock < 30;

        string summary = isStrong  ? "筹码承接较强，平台锁仓特征明显，短线抗跌" :
                         isDist    ? "存在派发嫌疑，主力可能在高位减仓，谨慎参与" :
                                     "筹码结构中性，需观察量能配合";

        return new ChipControlResult(support, chipLock, downRes, isStrong, isLocked, isDist, tags, summary);
    }
}
