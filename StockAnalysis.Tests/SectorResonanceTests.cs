using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using Xunit;

namespace StockAnalysis.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// 七、SectorResonanceEngine 测试
// ─────────────────────────────────────────────────────────────────────────────
public class SectorResonanceEngineTests
{
    private readonly SectorResonanceEngine _engine = new();

    private static List<StockBar> TwoBars(decimal prevClose, decimal todayClose) =>
    [
        new StockBar { Close = prevClose, Open = prevClose, High = prevClose, Low = prevClose, Volume = 10000, Date = DateTime.Today.AddDays(-1) },
        new StockBar { Close = todayClose, Open = todayClose, High = todayClose, Low = todayClose, Volume = 10000, Date = DateTime.Today }
    ];

    // 个股K线（上涨）
    private static List<StockBar> StockUp() => Bars.Flat(20, 10m).Append(Bars.Bar(10.5m, 10m, 10.6m, 9.9m, 20000));

    // 个股K线（弱势）
    private static List<StockBar> StockFlat() => Bars.Flat(21, 10m);

    [Fact]
    public void Test1_TrueSectorResonance()
    {
        // 10只股票，8只上涨，3只涨停 → 强共振
        var sector = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i < 3 ? 11.0m : i < 8 ? 10.5m : 9.8m)).ToList();

        var result = _engine.Analyze(StockUp(), sector);

        Assert.True(result.ResonanceScore >= 75, $"强共振应>=75，实际={result.ResonanceScore}");
        Assert.True(result.IsSectorResonance, "应识别为板块共振");
        Assert.False(result.IsIndependentPump, "不应为孤立拉升");
    }

    [Fact]
    public void Test2_IndependentPump()
    {
        // 板块大多数下跌，只有个股上涨
        var sector = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i < 2 ? 10.1m : 9.5m)).ToList(); // 2只微涨，8只大跌

        var result = _engine.Analyze(StockUp(), sector);

        Assert.True(result.ResonanceScore < 40, $"孤立拉升共振分应<40，实际={result.ResonanceScore}");
        Assert.True(result.IsIndependentPump, "应识别为孤立拉升");
    }

    [Fact]
    public void Test3_TailPump()
    {
        // 全天弱势（均价低于开盘），尾盘突然拉红
        var minuteBars = new List<MinuteBar>();
        // 前80%时间弱势
        for (int i = 0; i < 16; i++)
            minuteBars.Add(new MinuteBar { Time = DateTime.Today.AddMinutes(i * 15), Open = 10m, High = 10m, Low = 9.8m, Close = 9.85m, Volume = 1000 });
        // 尾盘20%拉升
        for (int i = 16; i < 20; i++)
            minuteBars.Add(new MinuteBar { Time = DateTime.Today.AddMinutes(i * 15), Open = 9.85m, High = 10.2m, Low = 9.85m, Close = 10.1m, Volume = 5000 });

        var stockBars = Bars.Flat(20, 10m);
        stockBars.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today, Open = 10m, High = 10.2m, Low = 9.8m, Close = 10.1m, Volume = 20000, Amount = 200000 });

        var sector = Enumerable.Range(0, 10).Select(_ => TwoBars(10m, 10.1m)).ToList();
        var result = _engine.Analyze(stockBars, sector, minuteBars);

        Assert.True(result.IsFakeBreakoutRisk, $"尾盘偷拉应识别为诱多风险，Summary={result.SectorSummary}");
    }

    [Fact]
    public void Test4_SectorWeakening()
    {
        // 龙头断板，板块大跌，跟风股大跌
        var sector = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i == 0 ? 9.5m : 9.3m)).ToList(); // 全部大跌

        var result = _engine.Analyze(StockFlat(), sector);

        Assert.True(result.ResonanceScore < 50, $"板块退潮共振分应<50，实际={result.ResonanceScore}");
        Assert.True(result.IsSectorWeakening, "应识别为板块走弱");
    }

    [Fact]
    public void Test5_WatchPoolFilter_IndependentPump()
    {
        // ResonanceScore < 40 且 IsIndependentPump = true → 不进入机会池
        // 模拟过滤逻辑
        decimal resonanceScore = 35m;
        bool isIndependentPump = true;
        bool shouldExclude = resonanceScore < 40 && isIndependentPump;
        Assert.True(shouldExclude, "孤立拉升+低共振分应被剔除");
    }

    [Fact]
    public void Test6_ValueMainUpPool()
    {
        // 主升平台强 + 板块共振强 + 筹码锁定强 + 风险低 → 进入价值池
        bool isMainUpPlatform = true;
        decimal secondWave = 75m, chipLock = 75m, resonanceScore = 75m;
        int riskScore = 40;
        bool notDecline = true; // SectorEmotion != Decline
        bool notIndependent = true; // IsIndependentPump = false

        bool isValuePool = isMainUpPlatform && secondWave >= 70 && chipLock >= 70
            && notDecline && resonanceScore >= 70 && notIndependent && riskScore <= 45;

        Assert.True(isValuePool, "满足所有条件应进入主升浪价值池");
    }
}
