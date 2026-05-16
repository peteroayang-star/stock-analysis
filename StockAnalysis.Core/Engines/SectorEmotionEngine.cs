using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public record SectorEmotionResult(
    string SectorName,
    SectorEmotionCycle Cycle,
    decimal SectorStrengthScore,
    int LimitUpCount,
    decimal RisingRatio,
    decimal LeadingStockStrength,
    decimal DivergenceRisk,
    List<string> Tags,
    string Summary);

public class SectorEmotionEngine
{
    public SectorEmotionResult Analyze(string? sectorName, List<List<StockBar>>? sectorStocks)
    {
        string name = sectorName ?? "未知板块";
        if (sectorStocks == null || sectorStocks.Count == 0)
            return new SectorEmotionResult(name, SectorEmotionCycle.Divergence, 50, 0, 0, 0, 50, [], "无板块数据，中性处理");

        int total = 0, rising = 0, limitUp = 0;
        decimal maxGain = 0;
        var tags = new List<string>();

        foreach (var stockBars in sectorStocks)
        {
            if (stockBars.Count < 2) continue;
            total++;
            var today = stockBars[^1];
            var prev  = stockBars[^2];
            if (prev.Close <= 0) continue;
            decimal gain = (today.Close - prev.Close) / prev.Close;
            if (gain > 0) rising++;
            if (gain >= 0.095m) limitUp++;
            if (gain > maxGain) maxGain = gain;
        }

        if (total == 0)
            return new SectorEmotionResult(name, SectorEmotionCycle.Divergence, 50, 0, 0, 0, 50, [], "无有效板块数据");

        decimal risingRatio = (decimal)rising / total;
        decimal score = Math.Clamp(risingRatio * 50 + ((decimal)limitUp / total) * 30 + Math.Min(maxGain / 0.2m, 1m) * 20, 0, 100);

        SectorEmotionCycle cycle;
        if (risingRatio < 0.2m)
            cycle = SectorEmotionCycle.IcePoint;
        else if (limitUp >= 3 && risingRatio > 0.7m)
            cycle = SectorEmotionCycle.Climax;
        else if (risingRatio > 0.6m && limitUp >= 1)
            cycle = SectorEmotionCycle.Consensus;
        else if (risingRatio > 0.4m && limitUp == 0)
            cycle = SectorEmotionCycle.Divergence;
        else if (risingRatio >= 0.2m && risingRatio <= 0.4m)
            cycle = SectorEmotionCycle.Recovery;
        else
            cycle = SectorEmotionCycle.Decline;

        decimal divRisk = cycle switch {
            SectorEmotionCycle.Climax    => 80,
            SectorEmotionCycle.Decline   => 75,
            SectorEmotionCycle.Divergence => 60,
            SectorEmotionCycle.Consensus => 40,
            SectorEmotionCycle.Recovery  => 30,
            _                            => 20
        };

        if (limitUp >= 3) tags.Add($"{limitUp}只涨停");
        if (risingRatio > 0.7m) tags.Add("板块普涨");
        if (risingRatio < 0.3m) tags.Add("板块普跌");

        string summary = cycle switch {
            SectorEmotionCycle.Climax    => "板块处于高潮期，多只涨停，情绪过热，不适合追高",
            SectorEmotionCycle.Consensus => "板块情绪一致向上，龙头带动跟风，可积极参与",
            SectorEmotionCycle.Divergence => "板块内部分化，龙头强但跟风掉队，只选龙头",
            SectorEmotionCycle.Recovery  => "板块处于修复阶段，止跌企稳，可轻仓观察",
            SectorEmotionCycle.Decline   => "板块情绪退潮，资金流出，降低仓位或只观察",
            SectorEmotionCycle.IcePoint  => "板块处于冰点，普遍下跌，暂不参与",
            _                            => "板块情绪中性"
        };

        return new SectorEmotionResult(name, cycle, score, limitUp, risingRatio, maxGain, divRisk, tags, summary);
    }
}
