using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public record SectorResonanceResult(
    bool IsSectorResonance,
    bool IsIndependentPump,
    bool IsSectorLeading,
    bool IsSectorWeakening,
    bool IsFakeBreakoutRisk,
    decimal ResonanceScore,
    decimal SectorSupportScore,
    decimal SectorFlowStrength,
    decimal FollowStrength,
    decimal LeadingStrength,
    decimal DivergenceRisk,
    int SectorRisingCount,
    int SectorFallingCount,
    int SectorLimitUpCount,
    int SectorStrongCount,
    string SectorTrend,
    string SectorSummary,
    List<string> Tags);

public class SectorResonanceEngine
{
    /// <summary>
    /// 分析个股与板块的共振关系
    /// </summary>
    /// <param name="stockBars">个股K线</param>
    /// <param name="sectorStocks">板块所有成分股K线列表</param>
    /// <param name="minuteBars">个股分时数据（用于尾盘偷拉识别）</param>
    public SectorResonanceResult Analyze(
        List<StockBar> stockBars,
        List<List<StockBar>>? sectorStocks,
        List<MinuteBar>? minuteBars = null)
    {
        if (sectorStocks == null || sectorStocks.Count == 0 || stockBars.Count < 2)
            return Empty("无板块数据");

        // ── 板块统计 ──────────────────────────────────────────
        int total = 0, rising = 0, falling = 0, limitUp = 0, strong = 0;
        decimal maxSectorGain = 0, sumGain = 0;

        foreach (var bars in sectorStocks)
        {
            if (bars.Count < 2) continue;
            total++;
            var today = bars[^1]; var prev = bars[^2];
            if (prev.Close <= 0) continue;
            decimal gain = (today.Close - prev.Close) / prev.Close;
            sumGain += gain;
            if (gain > 0) rising++;
            else if (gain < 0) falling++;
            if (gain >= 0.095m) { limitUp++; strong++; }
            else if (gain >= 0.03m) strong++;
            if (gain > maxSectorGain) maxSectorGain = gain;
        }

        if (total == 0) return Empty("无有效板块数据");

        decimal risingRatio = (decimal)rising / total;
        decimal avgSectorGain = sumGain / total;

        // ── 个股涨幅 ──────────────────────────────────────────
        var stockToday = stockBars[^1]; var stockPrev = stockBars[^2];
        decimal stockGain = stockPrev.Close > 0
            ? (stockToday.Close - stockPrev.Close) / stockPrev.Close : 0;

        // ── 龙头状态（板块最强股是否仍强）──────────────────────
        decimal leadingStrength = Math.Clamp(maxSectorGain / 0.10m * 100, 0, 100);
        bool sectorLeading = limitUp >= 1 && risingRatio > 0.5m;
        bool sectorWeakening = risingRatio < 0.3m || (limitUp == 0 && avgSectorGain < -0.01m);

        // ── 跟风强度（板块强势股比例）──────────────────────────
        decimal followStrength = Math.Clamp((decimal)strong / total * 100, 0, 100);

        // ── 板块资金强度（用涨停数+上涨比例估算）──────────────
        decimal sectorFlowStrength = Math.Clamp(
            risingRatio * 50 + ((decimal)limitUp / Math.Max(total, 1)) * 30 + Math.Min(maxSectorGain / 0.1m, 1m) * 20,
            0, 100);

        // ── 板块支撑度 ─────────────────────────────────────────
        decimal sectorSupportScore = Math.Clamp(risingRatio * 60 + (decimal)strong / total * 40, 0, 100);

        // ── 孤立拉升检测 ──────────────────────────────────────
        bool isIndependentPump = risingRatio < 0.3m && stockGain > 0.02m;

        // ── 尾盘偷拉检测 ──────────────────────────────────────
        bool isTailPump = false;
        if (minuteBars != null && minuteBars.Count >= 20)
        {
            // 前80%时间均价 vs 尾盘20%均价
            int cutoff = (int)(minuteBars.Count * 0.8);
            decimal earlyAvg = minuteBars.Take(cutoff).Average(b => b.Close);
            decimal tailAvg  = minuteBars.Skip(cutoff).Average(b => b.Close);
            decimal openPrice = minuteBars[0].Open;
            // 全天大部分时间弱势（均价低于开盘），尾盘突然拉升
            isTailPump = earlyAvg < openPrice * 0.995m && tailAvg > earlyAvg * 1.005m
                         && stockToday.Close > stockToday.Open;
        }

        // ── 放量长上影检测 ────────────────────────────────────
        bool isLongUpperShadow = false;
        if (stockBars.Count >= 5)
        {
            var b = stockToday;
            decimal range = b.High - b.Low;
            decimal upperShadow = b.High - Math.Max(b.Open, b.Close);
            decimal avgVol = (decimal)stockBars.TakeLast(10).Average(x => x.Volume);
            isLongUpperShadow = range > 0 && upperShadow / range > 0.5m && b.Volume > avgVol * 1.5m;
        }

        bool isFakeBreakoutRisk = isIndependentPump || isTailPump || isLongUpperShadow || sectorWeakening;

        // ── 个股与板块同步性 ──────────────────────────────────
        decimal syncScore = 50m;
        if (avgSectorGain != 0)
        {
            decimal syncRatio = stockGain / Math.Abs(avgSectorGain);
            // 同向且幅度相近 → 高分；反向 → 低分
            syncScore = avgSectorGain > 0 && stockGain > 0
                ? Math.Clamp(50 + syncRatio * 25, 0, 100)
                : avgSectorGain < 0 && stockGain < 0
                    ? 40m
                    : avgSectorGain > 0 && stockGain <= 0 ? 20m : 60m;
        }

        // ── ResonanceScore 计算 ───────────────────────────────
        // 板块上涨家数占比 20% + 涨停数量 15% + 龙头状态 20% + 资金流向 15% + 梯队完整度 15% + 同步性 15%
        decimal resonanceScore = Math.Clamp(
            risingRatio * 100 * 0.20m
            + Math.Min((decimal)limitUp / Math.Max(total, 1) * 100 * 3, 100) * 0.15m
            + leadingStrength * 0.20m
            + sectorFlowStrength * 0.15m
            + followStrength * 0.15m
            + syncScore * 0.15m,
            0, 100);

        // 孤立拉升/尾盘偷拉惩罚
        if (isIndependentPump) resonanceScore = Math.Min(resonanceScore, 45);
        if (isTailPump) resonanceScore = Math.Min(resonanceScore, 50);

        bool isSectorResonance = resonanceScore >= 65 && !isIndependentPump;

        // ── 板块趋势描述 ──────────────────────────────────────
        string sectorTrend = risingRatio >= 0.7m && limitUp >= 2 ? "强势普涨"
            : risingRatio >= 0.6m ? "多数上涨"
            : risingRatio >= 0.4m ? "分化震荡"
            : risingRatio >= 0.2m ? "偏弱修复"
            : "普遍下跌";

        // ── 标签 ──────────────────────────────────────────────
        var tags = new List<string>();
        if (isSectorResonance)   tags.Add("板块共振");
        if (sectorLeading)       tags.Add("龙头跟随");
        if (isIndependentPump)   tags.Add("孤立拉升");
        if (isTailPump)          tags.Add("尾盘偷拉");
        if (sectorWeakening)     tags.Add("板块退潮");
        if (isFakeBreakoutRisk && !isIndependentPump && !isTailPump) tags.Add("诱多风险");

        // ── 摘要 ──────────────────────────────────────────────
        string summary = isSectorResonance
            ? $"该股上涨得到板块整体支撑，板块上涨{rising}只/{total}只，涨停{limitUp}只，龙头持续强势，存在明显共振，不属于孤立拉升。"
            : isIndependentPump
                ? "该股当前上涨缺乏板块支撑，板块整体走弱且资金流出，疑似主力自救或诱多，谨慎追高。"
                : isTailPump
                    ? "该股全天弱势，尾盘突然拉红，疑似尾盘偷拉，需警惕次日低开风险。"
                    : $"板块共振度一般（{resonanceScore:F0}分），板块上涨{rising}只/{total}只，建议观察板块整体走势后再决策。";

        decimal divRisk = Math.Clamp(100 - resonanceScore, 0, 100);

        return new SectorResonanceResult(
            isSectorResonance, isIndependentPump, sectorLeading, sectorWeakening, isFakeBreakoutRisk,
            Math.Round(resonanceScore, 1), Math.Round(sectorSupportScore, 1),
            Math.Round(sectorFlowStrength, 1), Math.Round(followStrength, 1),
            Math.Round(leadingStrength, 1), Math.Round(divRisk, 1),
            rising, falling, limitUp, strong,
            sectorTrend, summary, tags);
    }

    private static SectorResonanceResult Empty(string reason) =>
        new(false, false, false, false, false, 50, 50, 50, 50, 50, 50, 0, 0, 0, 0, "未知", reason, []);
}
