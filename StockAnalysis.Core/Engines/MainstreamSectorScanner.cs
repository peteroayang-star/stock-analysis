using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public record MainstreamSectorResult(
    string SectorName,
    decimal SectorHeatScore,
    decimal SectorCapitalFlow,
    decimal SectorTrendStrength,
    decimal SectorContinuity,
    decimal SectorLeaderStrength,
    int RisingCount,
    int FallingCount,
    int LimitUpCount,
    int StrongStockCount,
    bool IsMainstream,
    bool IsHotSector,
    bool IsDeclining,
    string Summary);

public class MainstreamSectorScanner
{
    /// <summary>
    /// 评估单个板块热度，返回 MainstreamSectorResult
    /// </summary>
    public MainstreamSectorResult Evaluate(string sectorName, List<List<StockBar>> sectorStocks)
    {
        if (sectorStocks == null || sectorStocks.Count == 0)
            return Empty(sectorName);

        int total = 0, rising = 0, falling = 0, limitUp = 0, strong = 0;
        decimal maxGain = 0, sumGain = 0;

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
            if (gain > maxGain) maxGain = gain;
        }

        if (total == 0) return Empty(sectorName);

        decimal risingRatio = (decimal)rising / total;
        decimal avgGain = sumGain / total;

        // 龙头强度：最大涨幅 / 10%
        decimal leaderStrength = Math.Clamp(maxGain / 0.10m * 100, 0, 100);
        // 趋势强度：上涨比例 + 涨停贡献
        decimal trendStrength = Math.Clamp(risingRatio * 60 + (decimal)limitUp / Math.Max(total, 1) * 40 * 10, 0, 100);
        // 资金流向估算（涨停数+强势股比例）
        decimal capitalFlow = Math.Clamp(
            (decimal)limitUp / Math.Max(total, 1) * 50 + (decimal)strong / total * 50, 0, 100);
        // 连续性（用当日平均涨幅估算，正向加分）
        decimal continuity = Math.Clamp(50 + avgGain * 500, 0, 100);

        // SectorHeatScore = 上涨比20% + 涨停15% + 龙头20% + 资金15% + 强势股15% + 趋势15%
        decimal heatScore = Math.Clamp(
            risingRatio * 100 * 0.20m
            + Math.Min((decimal)limitUp / Math.Max(total, 1) * 100 * 3, 100) * 0.15m
            + leaderStrength * 0.20m
            + capitalFlow * 0.15m
            + (decimal)strong / total * 100 * 0.15m
            + trendStrength * 0.15m,
            0, 100);

        bool isMainstream = heatScore >= 80;
        bool isHotSector  = heatScore >= 65;
        bool isDeclining  = risingRatio < 0.3m || (limitUp == 0 && avgGain < -0.005m);

        string summary = isMainstream ? $"主线板块，上涨{rising}/{total}只，涨停{limitUp}只，热度{heatScore:F0}分"
            : isHotSector ? $"强势板块，上涨{rising}/{total}只，涨停{limitUp}只"
            : isDeclining ? $"板块退潮，上涨{rising}/{total}只，热度偏低"
            : $"一般板块，热度{heatScore:F0}分";

        return new MainstreamSectorResult(
            sectorName, Math.Round(heatScore, 1), Math.Round(capitalFlow, 1),
            Math.Round(trendStrength, 1), Math.Round(continuity, 1), Math.Round(leaderStrength, 1),
            rising, falling, limitUp, strong,
            isMainstream, isHotSector, isDeclining, summary);
    }

    private static MainstreamSectorResult Empty(string name) =>
        new(name, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, true, "无板块数据");
}
