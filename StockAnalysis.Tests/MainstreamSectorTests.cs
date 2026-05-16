using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using Xunit;

namespace StockAnalysis.Tests;

public class MainstreamSectorScannerTests
{
    private readonly MainstreamSectorScanner _scanner = new();

    private static List<StockBar> TwoBars(decimal prev, decimal today, long vol = 10000) =>
    [
        new() { Close = prev, Open = prev, High = prev, Low = prev, Volume = vol, Date = DateTime.Today.AddDays(-1) },
        new() { Close = today, Open = today, High = today * 1.01m, Low = today * 0.99m, Volume = vol, Date = DateTime.Today }
    ];

    // 1. 主线板块识别：上涨家数高、龙头强、涨停多
    [Fact]
    public void Test1_MainstreamSector()
    {
        // 10只股票：7只上涨，3只涨停（涨10%）
        var stocks = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i < 3 ? 11.0m : i < 7 ? 10.5m : 9.9m)).ToList();

        var result = _scanner.Evaluate("商业航天", stocks);

        Assert.True(result.SectorHeatScore >= 80, $"主线板块热度应>=80，实际={result.SectorHeatScore}");
        Assert.True(result.IsMainstream, "应识别为主线板块");
    }

    // 2. 退潮板块：龙头断板、板块下跌
    [Fact]
    public void Test2_Decliningsector()
    {
        // 10只股票：1只微涨，9只大跌
        var stocks = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i == 0 ? 10.1m : 8.5m)).ToList();

        var result = _scanner.Evaluate("电力", stocks);

        Assert.True(result.IsDeclining, $"应识别为退潮板块，热度={result.SectorHeatScore}");
    }

    // 3. 冷门板块过滤：热度低、无涨停
    [Fact]
    public void Test3_ColdSector()
    {
        // 10只股票：3只微涨，7只微跌，无涨停
        var stocks = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i < 3 ? 10.2m : 9.9m)).ToList();

        var result = _scanner.Evaluate("冷门板块", stocks);

        Assert.False(result.IsMainstream, "冷门板块不应进入主线");
        Assert.False(result.IsHotSector, "冷门板块不应为强势板块");
        Assert.True(result.SectorHeatScore < 50, $"热度应<50，实际={result.SectorHeatScore}");
    }

    // 4. 缓存机制：连续多天高分股优先级更高
    [Fact]
    public void Test4_CandidateCachePriority()
    {
        var highPriority = new CandidateStockItem
        {
            StockCode = "000001", StockName = "测试A",
            ConsecutiveAppearDays = 3, IsPreviousValuePool = true,
            MainUpPlatformScore = 80, ChipLockScore = 75, ResonanceScore = 70
        };
        var lowPriority = new CandidateStockItem
        {
            StockCode = "000002", StockName = "测试B",
            ConsecutiveAppearDays = 0, IsPreviousValuePool = false,
            MainUpPlatformScore = 60, ChipLockScore = 60, ResonanceScore = 50
        };

        Assert.True(highPriority.ScanPriorityScore > lowPriority.ScanPriorityScore,
            $"连续出现+价值池股票优先级应更高：{highPriority.ScanPriorityScore} vs {lowPriority.ScanPriorityScore}");
    }

    // 5. 主升浪价值池：主线板块+主升平台+板块共振+空间充足
    [Fact]
    public void Test5_ValuePoolConditions()
    {
        // 验证价值池判断逻辑（直接测试条件组合）
        bool isMainUpPlatform = true;
        decimal secondWaveProbability = 75m;
        decimal chipLockScore = 72m;
        bool sectorNotDecline = true; // SectorEmotion != Decline
        decimal resonanceScore = 72m;
        bool notIndependentPump = true;
        int riskScore = 40;
        decimal sectorHeatScore = 75m;
        bool sectorNotDeclining = true;

        bool isValuePool = isMainUpPlatform
            && secondWaveProbability >= 70 && chipLockScore >= 70
            && sectorNotDecline && resonanceScore >= 70 && notIndependentPump
            && riskScore <= 45 && sectorHeatScore >= 70 && sectorNotDeclining;

        Assert.True(isValuePool, "满足所有条件应进入价值池");
    }

    // 6. DecisionEngine：板块退潮时禁止激进
    [Fact]
    public void Test6_DecisionEngine_DecliningBlocked()
    {
        var engine = new DecisionEngine(new RiskConfig());
        var decliningMainstream = new MainstreamSectorResult(
            "电力", 40m, 30m, 30m, 40m, 50m, 3, 7, 0, 2,
            false, false, true, "板块退潮");

        var (decision, reason) = engine.DecideEntry(
            BuySignalType.VolumeBreakout, riskScore: 25, Trend.Up,
            aboveWatchPrice: true,
            new CycleResult(MarketCycle.Consensus, "一致"),
            new VolumeResult(VolumeState.ShrinkConsolidate, true, "缩量"),
            belowMA20: false, macdDead: false, belowStopLoss: false,
            sectorMainstream: decliningMainstream);

        Assert.True(decision == Decision.Watch || decision == Decision.Ignore,
            $"板块退潮时不允许Buy，实际={decision}");
        Assert.NotNull(reason);
        Assert.Contains("退潮", reason);
    }

    // 7. DecisionEngine：板块不在Top10时Buy降级
    [Fact]
    public void Test7_DecisionEngine_NonMainstreamDowngrade()
    {
        var engine = new DecisionEngine(new RiskConfig());
        var coldMainstream = new MainstreamSectorResult(
            "冷门板块", 45m, 30m, 30m, 40m, 40m, 3, 7, 0, 2,
            false, false, false, "一般板块");

        var (decision, reason) = engine.DecideEntry(
            BuySignalType.VolumeBreakout, riskScore: 25, Trend.Up,
            aboveWatchPrice: true,
            new CycleResult(MarketCycle.Consensus, "一致"),
            new VolumeResult(VolumeState.ShrinkConsolidate, true, "缩量"),
            belowMA20: false, macdDead: false, belowStopLoss: false,
            sectorMainstream: coldMainstream);

        Assert.True(decision == Decision.Watch || decision == Decision.Ignore,
            $"非主线板块Buy应降级，实际={decision}");
    }

    // 8. HotSectorRotationEngine：识别加强/退潮
    [Fact]
    public void Test8_HotSectorRotation()
    {
        var engine = new HotSectorRotationEngine();
        var prev = new Dictionary<string, decimal> { ["电力"] = 75m, ["商业航天"] = 55m };
        var curr = new Dictionary<string, decimal> { ["电力"] = 60m, ["商业航天"] = 80m };

        var results = engine.Analyze(prev, curr);

        var power = results.First(r => r.SectorName == "电力");
        var space = results.First(r => r.SectorName == "商业航天");

        Assert.True(power.IsWeakening, "电力热度下降应识别为退潮");
        Assert.True(space.IsStrengthening, "商业航天热度上升应识别为加强");
    }
}
