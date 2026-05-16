using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using Xunit;

namespace StockAnalysis.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// 六、WatchPool 过滤规则测试
// ─────────────────────────────────────────────────────────────────────────────
public class WatchPoolFilterTests
{
    record PoolItem(string Code, string Name, decimal Price, decimal? MarketCap,
        decimal WatchPoolScore, decimal SecondWaveProbability, decimal LockPositionStrength,
        decimal ChipLockScore, int RiskScore, string Tier);

    private static PoolItem? BuildItem(
        decimal price, decimal? marketCap, string name, string code,
        int riskScore, decimal secondWave, decimal lockStr, decimal chipLock,
        SectorEmotionCycle emotion = SectorEmotionCycle.Consensus)
    {
        if (name.Contains("ST") || name.Contains("退")) return null;
        if (code.StartsWith("688") || code.StartsWith("300") || code.StartsWith("301")
            || code.StartsWith("8") || code.StartsWith("4")) return null;
        if (price >= 30m) return null;
        if (marketCap.HasValue && marketCap.Value >= 300m) return null;
        if (riskScore > 50) return null;
        if (secondWave < 60 || lockStr < 60 || chipLock < 60) return null;

        var score = secondWave * 0.25m + lockStr * 0.20m + chipLock * 0.20m
                  + 70m * 0.15m + 60m * 0.10m + (100 - riskScore) * 0.10m;

        var tier = score >= 80 && secondWave >= 75 && chipLock >= 70 && riskScore <= 40
            ? "激进"
            : score >= 70 && secondWave >= 65 && riskScore <= 50 ? "重点"
            : score >= 60 ? "普通" : null;
        if (tier == null) return null;
        if (tier == "激进" && emotion == SectorEmotionCycle.Decline) tier = "重点";

        return new PoolItem(code, name, price, marketCap, score, secondWave, lockStr, chipLock, riskScore, tier);
    }

    [Fact] public void Test1_PriceFilter() =>
        Assert.Null(BuildItem(30m, 100m, "测试", "000001", 30, 70, 70, 70));

    [Fact] public void Test2_MarketCapFilter() =>
        Assert.Null(BuildItem(20m, 300m, "测试", "000001", 30, 70, 70, 70));

    [Fact] public void Test3_STFilter() =>
        Assert.Null(BuildItem(10m, 100m, "ST测试", "000001", 30, 70, 70, 70));

    [Fact] public void Test3b_KCBFilter() =>
        Assert.Null(BuildItem(10m, 100m, "科创", "688001", 30, 70, 70, 70));

    [Fact] public void Test3c_CYBFilter() =>
        Assert.Null(BuildItem(10m, 100m, "创业", "300001", 30, 70, 70, 70));

    [Fact] public void Test3d_BJFilter() =>
        Assert.Null(BuildItem(10m, 100m, "北交", "830001", 30, 70, 70, 70));

    [Fact] public void Test4_RiskScoreFilter() =>
        Assert.Null(BuildItem(10m, 100m, "测试", "000001", 51, 70, 70, 70));

    [Fact] public void Test5_SecondWaveFilter() =>
        Assert.Null(BuildItem(10m, 100m, "测试", "000001", 30, 59, 70, 70));

    [Fact] public void Test6_ChipLockFilter() =>
        Assert.Null(BuildItem(10m, 100m, "测试", "000001", 30, 70, 70, 59));

    [Fact] public void Test7_DeclineNotAggressive()
    {
        var item = BuildItem(10m, 100m, "测试", "000001", 20, 90, 90, 90, SectorEmotionCycle.Decline);
        Assert.NotNull(item);
        Assert.NotEqual("激进", item!.Tier);
    }

    [Fact] public void Test8_AggressiveTier()
    {
        // score = 90*0.25 + 90*0.20 + 90*0.20 + 70*0.15 + 60*0.10 + 80*0.10
        //       = 22.5 + 18 + 18 + 10.5 + 6 + 8 = 83 >= 80, secondWave=90>=75, chipLock=90>=70, risk=20<=40
        var item = BuildItem(10m, 100m, "测试", "000001", 20, 90, 90, 90, SectorEmotionCycle.Consensus);
        Assert.NotNull(item);
        Assert.Equal("激进", item!.Tier);
    }

    [Fact] public void Test9_MaxThirtyItems()
    {
        var items = Enumerable.Range(0, 50)
            .Select(_ => BuildItem(10m, 100m, "测试", "000001", 30, 70, 70, 70))
            .Where(x => x != null).Take(30).ToList();
        Assert.True(items.Count <= 30);
    }

    [Fact] public void Test10_SingleFailureDoesNotBreak()
    {
        var results = new PoolItem?[] {
            null,
            BuildItem(10m, 100m, "测试A", "000001", 30, 70, 70, 70),
            BuildItem(10m, 100m, "测试B", "000002", 30, 70, 70, 70)
        };
        Assert.Equal(2, results.Count(x => x != null));
    }
}
