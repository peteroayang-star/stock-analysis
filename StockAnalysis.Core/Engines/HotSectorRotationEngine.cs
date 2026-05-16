namespace StockAnalysis.Core.Engines;

public record SectorRotationResult(
    string SectorName,
    decimal PreviousHeatScore,
    decimal CurrentHeatScore,
    bool IsStrengthening,
    bool IsWeakening,
    string Summary);

/// <summary>热点轮动跟踪：识别市场主线是否切换，自动调整板块权重</summary>
public class HotSectorRotationEngine
{
    /// <summary>
    /// 对比两天的板块热度，识别加强/退潮板块
    /// </summary>
    public List<SectorRotationResult> Analyze(
        Dictionary<string, decimal> previousScores,
        Dictionary<string, decimal> currentScores)
    {
        var results = new List<SectorRotationResult>();
        var allSectors = previousScores.Keys.Union(currentScores.Keys);

        foreach (var sector in allSectors)
        {
            var prev = previousScores.GetValueOrDefault(sector, 0);
            var curr = currentScores.GetValueOrDefault(sector, 0);
            var delta = curr - prev;

            bool strengthening = delta >= 10 || (prev < 65 && curr >= 65);
            bool weakening = delta <= -10 || (prev >= 65 && curr < 65);

            var summary = strengthening ? $"【加强】{sector}热度+{delta:F0}分，资金流入"
                : weakening ? $"【退潮】{sector}热度{delta:F0}分，资金撤离"
                : $"{sector}热度稳定({curr:F0}分)";

            results.Add(new SectorRotationResult(sector, prev, curr, strengthening, weakening, summary));
        }

        return results.OrderByDescending(r => r.CurrentHeatScore).ToList();
    }

    /// <summary>根据轮动结果计算板块权重调整系数（1.0=不变，>1=提升，<1=降低）</summary>
    public decimal GetWeightMultiplier(SectorRotationResult rotation) =>
        rotation.IsStrengthening ? 1.2m :
        rotation.IsWeakening ? 0.7m : 1.0m;
}
