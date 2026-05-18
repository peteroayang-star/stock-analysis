using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>风险股标签引擎：识别6类风险特征，不删除股票但在机会池中降权标记</summary>
public class RiskStockTagEngine
{
    /// <summary>评估一只股票的风险标签</summary>
    public List<RiskTag> Evaluate(List<StockBar> bars, StockSignal signal, decimal? marketCap, decimal? profitYoy)
    {
        var tags = new List<RiskTag>();

        if (bars.Count < 5) return tags;

        var latest = bars[^1];
        var recent5 = bars.Skip(bars.Count - 5).ToList();

        DetectLowPriceSurge(bars, recent5, latest, tags);
        DetectPreviouslyST(tags);
        DetectEarningsDecline(profitYoy, tags);
        DetectConsecutiveLimitUps(signal, latest, tags);
        DetectAnnouncementRisk(signal, tags);
        DetectPoorLiquidity(bars, marketCap, tags);

        return tags;
    }

    private static void DetectLowPriceSurge(List<StockBar> bars, List<StockBar> recent5, StockBar latest, List<RiskTag> tags)
    {
        if (latest.Close >= 8m) return;

        var firstClose = recent5[0].Close;
        if (firstClose <= 0) return;
        var gain5 = (latest.Close - firstClose) / firstClose * 100;
        if (gain5 > 15m)
        {
            var avgVol = bars.Skip(bars.Count - 20).Take(15).Average(b => b.Volume);
            if (recent5.Average(b => b.Volume) > avgVol * 2)
            {
                tags.Add(new RiskTag(RiskTagType.LowPriceSurge, "低价暴涨", 3,
                    $"股价{latest.Close:F2}元，5日涨幅{gain5:F0}%且放量，疑似游资炒作，追高风险极大"));
            }
        }
    }

    private static void DetectPreviouslyST(List<RiskTag> tags)
    {
        // 当前ST已被StockFilter过滤，此处标记无操作
        // 保留接口供后续引入曾ST名单缓存
    }

    private static void DetectEarningsDecline(decimal? profitYoy, List<RiskTag> tags)
    {
        if (profitYoy == null) return;

        if (profitYoy < -30m)
        {
            tags.Add(new RiskTag(RiskTagType.EarningsDecline, "业绩下滑", 2,
                $"最近季度净利润同比{profitYoy:F0}%，基本面恶化风险"));
        }
    }

    private static void DetectConsecutiveLimitUps(StockSignal signal, StockBar latest, List<RiskTag> tags)
    {
        if (signal.LimitUpCountIn14Days >= 3 && latest.Close > 0)
        {
            tags.Add(new RiskTag(RiskTagType.ConsecutiveLimitUps, "连续异常涨停", 3,
                $"14天内涨停{signal.LimitUpCountIn14Days}次，存在高位接盘和监管关注风险"));
        }
    }

    private static void DetectAnnouncementRisk(StockSignal signal, List<RiskTag> tags)
    {
        if (signal.VolatilityRisk > 70 && signal.SentimentRisk > 50)
        {
            tags.Add(new RiskTag(RiskTagType.AnnouncementRisk, "异常波动", 2,
                $"波动风险{signal.VolatilityRisk}，情绪风险{signal.SentimentRisk}，可能存在未公告事项"));
        }
    }

    private static void DetectPoorLiquidity(List<StockBar> bars, decimal? marketCap, List<RiskTag> tags)
    {
        var recent20 = bars.Skip(bars.Count - 20).ToList();
        var avgAmount = recent20.Average(b => b.Amount);
        if (avgAmount < 30_000_000m)
        {
            tags.Add(new RiskTag(RiskTagType.PoorLiquidity, "流动性差", 1,
                $"近20日均成交额{avgAmount / 10000:F0}万，流动性不足，进出困难"));
        }
    }
}
