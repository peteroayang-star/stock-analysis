using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using Xunit;

namespace StockAnalysis.Tests;

/// <summary>构造测试用 K 线数据的辅助方法</summary>
static class Bars
{
    /// <summary>生成 count 根平稳 K 线，收盘价 = basePrice</summary>
    public static List<StockBar> Flat(int count, decimal basePrice = 10m, string code = "000001")
    {
        var list = new List<StockBar>();
        for (int i = 0; i < count; i++)
            list.Add(new StockBar { Code = code, Name = "测试", Date = DateTime.Today.AddDays(i - count),
                Open = basePrice, High = basePrice * 1.01m, Low = basePrice * 0.99m,
                Close = basePrice, Volume = 10000, Amount = basePrice * 10000 });
        return list;
    }

    /// <summary>在 base 列表末尾追加 K 线</summary>
    public static List<StockBar> Append(this List<StockBar> bars, StockBar bar) { bars.Add(bar); return bars; }

    public static StockBar Bar(decimal close, decimal open = 0, decimal high = 0, decimal low = 0,
        long vol = 10000, string code = "000001") =>
        new() { Code = code, Name = "测试", Date = DateTime.Today,
            Open = open > 0 ? open : close, High = high > 0 ? high : close * 1.01m,
            Low = low > 0 ? low : close * 0.99m, Close = close, Volume = vol, Amount = close * vol };

    /// <summary>生成主升浪：前20日从 basePrice 涨到 basePrice*1.35，含1根涨停</summary>
    public static List<StockBar> WithMainUp(int totalBars = 40, decimal basePrice = 10m)
    {
        var list = new List<StockBar>();
        // 前10根平稳底部
        for (int i = 0; i < 10; i++)
            list.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today.AddDays(i - totalBars),
                Open = basePrice, High = basePrice * 1.01m, Low = basePrice * 0.99m,
                Close = basePrice, Volume = 10000, Amount = basePrice * 10000 });
        // 主升浪：20根，含1根涨停
        decimal price = basePrice;
        for (int i = 0; i < 20; i++)
        {
            decimal gain = i == 5 ? 0.10m : 0.015m; // 第6根涨停
            decimal prev = price;
            price *= (1 + gain);
            list.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today.AddDays(i + 10 - totalBars),
                Open = prev, High = price * 1.005m, Low = prev * 0.995m,
                Close = price, Volume = i == 5 ? 50000 : 20000, Amount = price * 20000 });
        }
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 一、MainUpPlatformEngine 测试
// ─────────────────────────────────────────────────────────────────────────────
public class MainUpPlatformEngineTests
{
    private readonly MainUpPlatformEngine _engine = new();
    private readonly IndicatorCalculator _calc = new();
    private readonly VolumeEngine _vol = new();
    private readonly CycleDetector _cycle = new();

    private (MainUpPlatformResult result, IndicatorCalculator.Indicators ind) Analyze(List<StockBar> bars)
    {
        int idx = bars.Count - 1;
        var vol = _vol.Analyze(bars, idx);
        var cycle = _cycle.Detect(bars, idx, vol);
        var ind = _calc.Calculate(bars, idx);
        return (_engine.Analyze(bars, idx, ind, vol, cycle, 20), ind);
    }

    [Fact]
    public void TestA_HealthyMainUpPlatform()
    {
        // 主升浪后横盘5天缩量
        var bars = Bars.WithMainUp(40);
        decimal platPrice = bars[^1].Close;
        // 追加5根缩量横盘
        for (int i = 0; i < 5; i++)
            bars.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today.AddDays(i - 4),
                Open = platPrice * 0.995m, High = platPrice * 1.005m, Low = platPrice * 0.99m,
                Close = platPrice * (1 + (i % 2 == 0 ? 0.002m : -0.001m)),
                Volume = 5000, Amount = platPrice * 5000 });

        var (result, _) = Analyze(bars);

        Assert.True(result.IsMainUpPlatform, $"应识别为主升平台，Summary={result.Summary}");
        Assert.True(result.LockPositionStrength >= 70, $"锁仓强度应>=70，实际={result.LockPositionStrength}");
        Assert.True(result.SecondWaveProbability >= 65, $"二波概率应>=65，实际={result.SecondWaveProbability}");
    }

    [Fact]
    public void TestB_DistributionPlatform()
    {
        // 主升浪后放量滞涨+长上影
        var bars = Bars.WithMainUp(40);
        decimal platPrice = bars[^1].Close;
        for (int i = 0; i < 5; i++)
            bars.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today.AddDays(i - 4),
                Open = platPrice, High = platPrice * 1.06m, Low = platPrice * 0.98m,
                Close = platPrice * 1.002m, // 放量但涨幅极小
                Volume = 80000, Amount = platPrice * 80000 });

        var (result, _) = Analyze(bars);

        Assert.False(result.IsMainUpPlatform, $"放量滞涨不应识别为健康平台，Summary={result.Summary}");
        Assert.True(result.SecondWaveProbability < 50, $"二波概率应<50，实际={result.SecondWaveProbability}");
        Assert.True(result.Tags.Any(t => t.Contains("放量") || t.Contains("派发") || t.Contains("上影")),
            $"Tags应包含放量/派发/上影，实际={string.Join(",", result.Tags)}");
    }

    [Fact]
    public void TestC_NoPriorMainUp()
    {
        // 没有主升浪，普通横盘
        var bars = Bars.Flat(40, 10m);

        var (result, _) = Analyze(bars);

        Assert.False(result.IsMainUpPlatform);
        // 无主升浪时，Summary 可能是"未形成主升浪基础"或其他排除原因
        Assert.False(result.IsMainUpPlatform, $"普通横盘不应识别为主升平台，Summary={result.Summary}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 二、DragonTigerBehaviorEngine 测试
// ─────────────────────────────────────────────────────────────────────────────
public class DragonTigerBehaviorEngineTests
{
    private readonly DragonTigerBehaviorEngine _engine = new();

    private static DragonTigerRecord MakeRecord(DateTime date, decimal buy, decimal sell,
        string seatType = "HotMoney", string seatName = "游资A", bool famous = true) =>
        new("000001", "测试", date, buy, sell, buy - sell,
            [new DragonTigerSeat(seatName, Enum.Parse<SeatType>(seatType), buy, famous, false)],
            []);

    [Fact]
    public void TestA_HotMoneyRelay()
    {
        var records = new List<DragonTigerRecord>
        {
            MakeRecord(DateTime.Today.AddDays(-8), 5000_0000, 1000_0000, "HotMoney", "游资A", true),
            MakeRecord(DateTime.Today.AddDays(-5), 4000_0000, 800_0000,  "HotMoney", "游资B", true),
            MakeRecord(DateTime.Today.AddDays(-2), 6000_0000, 500_0000,  "HotMoney", "游资A", true),
        };

        var result = _engine.Analyze(records);

        Assert.True(result.IsOnDragonTigerList);
        Assert.True(result.IsRepeatedSeat, "近10日上榜>=3次应为反复上榜");
        Assert.True(result.RelayStrength >= 70, $"接力强度应>=70，实际={result.RelayStrength}");
    }

    [Fact]
    public void TestB_OneDayTour()
    {
        // 只上榜1次，净买入为负 → 一日游
        var singleRecord = new DragonTigerRecord("000001", "测试",
            DateTime.Today.AddDays(-1), 1000_0000, 9000_0000, -8000_0000, [], []);

        var result = _engine.Analyze([singleRecord]);

        Assert.True(result.IsOneDayTour, $"应识别为一日游，Summary={result.Summary}");
        Assert.Contains("一日游", result.Summary);
    }

    [Fact]
    public void TestC_NoData()
    {
        var result = _engine.Analyze(null);

        Assert.False(result.IsOnDragonTigerList);
        Assert.Contains("暂无龙虎榜数据", result.Summary);

        var result2 = _engine.Analyze([]);
        Assert.False(result2.IsOnDragonTigerList);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 三、SectorEmotionEngine 测试
// ─────────────────────────────────────────────────────────────────────────────
public class SectorEmotionEngineTests
{
    private readonly SectorEmotionEngine _engine = new();

    private static List<StockBar> TwoBars(decimal prevClose, decimal todayClose) =>
    [
        new StockBar { Close = prevClose, Open = prevClose, High = prevClose, Low = prevClose, Volume = 10000, Date = DateTime.Today.AddDays(-1) },
        new StockBar { Close = todayClose, Open = todayClose, High = todayClose, Low = todayClose, Volume = 10000, Date = DateTime.Today }
    ];

    [Fact]
    public void TestA_Consensus()
    {
        // 10只股票，8只上涨，3只涨停（risingRatio=0.8 > 0.6, limitUp=3 → Climax）
        // 调整为 2只涨停，8只上涨 → Consensus
        var sector = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i < 2 ? 11.0m : i < 9 ? 10.5m : 9.8m)).ToList();

        var result = _engine.Analyze("测试板块", sector);

        Assert.Equal(SectorEmotionCycle.Consensus, result.Cycle);
        Assert.True(result.SectorStrengthScore >= 60, $"板块强度应>=60，实际={result.SectorStrengthScore}");
    }

    [Fact]
    public void TestB_Climax()
    {
        // 10只股票，4只涨停，8只上涨
        var sector = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i < 4 ? 11.0m : i < 8 ? 10.3m : 9.9m)).ToList();

        var result = _engine.Analyze("测试板块", sector);

        Assert.Equal(SectorEmotionCycle.Climax, result.Cycle);
        Assert.Contains("高潮", result.Summary);
    }

    [Fact]
    public void TestC_Decline()
    {
        // 10只股票，1只微涨，9只大跌（risingRatio=0.1 < 0.2 → IcePoint，或 0.1~0.2 → Recovery）
        // 用 risingRatio=0.1 触发 IcePoint，再用 Decline 场景：risingRatio=0.25 但有大跌
        // SectorEmotionEngine: risingRatio < 0.2 → IcePoint; 0.2~0.4 → Recovery; else Decline
        // 要触发 Decline：risingRatio > 0.4 但 limitUp=0 不满足 Consensus，且不满足其他条件
        // 实际逻辑：risingRatio > 0.4 && limitUp == 0 → Divergence; else → Decline
        // 所以 Decline = risingRatio 在 0.2~0.4 之间且不满足 Recovery 条件 → 实际是 Recovery
        // 引擎逻辑：risingRatio >= 0.2 && <= 0.4 → Recovery; else → Decline
        // 要触发 Decline：risingRatio > 0.4 && limitUp == 0 → Divergence（不是Decline）
        // Decline 触发条件：else 分支，即 risingRatio > 0.4 && limitUp >= 1 && < 3 && risingRatio <= 0.6
        // 用 6只上涨（ratio=0.6），1只涨停 → Consensus（ratio>0.6 && limitUp>=1）
        // 实际 Decline = 当 risingRatio 在 (0.4, 0.6] 且 limitUp == 0 → Divergence
        // 重新看引擎代码：else → Decline，即不满足前面所有条件时
        // 条件顺序：IcePoint(<0.2) → Climax(>=3涨停&&>0.7) → Consensus(>0.6&&>=1) → Divergence(>0.4&&==0) → Recovery(0.2~0.4) → else=Decline
        // Decline 触发：risingRatio > 0.4 && limitUp >= 1 && (risingRatio <= 0.6 || limitUp < 3)
        // 用 5只上涨(0.5)，1只涨停 → else → Decline ✓
        var sector = Enumerable.Range(0, 10).Select(i =>
            TwoBars(10m, i == 0 ? 11.0m : i < 5 ? 10.3m : 9.3m)).ToList();

        var result = _engine.Analyze("测试板块", sector);

        Assert.Equal(SectorEmotionCycle.Decline, result.Cycle);
        Assert.True(result.DivergenceRisk >= 70, $"分歧风险应>=70，实际={result.DivergenceRisk}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 四、ChipControlEngine 测试
// ─────────────────────────────────────────────────────────────────────────────
public class ChipControlEngineTests
{
    private readonly ChipControlEngine _engine = new();
    private readonly IndicatorCalculator _calc = new();
    private readonly VolumeEngine _volEngine = new();

    [Fact]
    public void TestA_StrongSupport()
    {
        // 缩量回踩MA5后收回，低点逐步抬高
        var bars = Bars.WithMainUp(40);
        decimal base_ = bars[^1].Close;
        // 追加5根：低点抬高，缩量，收盘在MA5上方
        for (int i = 0; i < 5; i++)
        {
            decimal low = base_ * (0.97m + i * 0.003m);
            decimal close = base_ * (0.995m + i * 0.002m);
            bars.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today.AddDays(i - 4),
                Open = close * 0.998m, High = close * 1.005m, Low = low,
                Close = close, Volume = 4000 - i * 200, Amount = close * 4000 });
        }
        int idx = bars.Count - 1;
        var vol = _volEngine.Analyze(bars, idx);
        var ind = _calc.Calculate(bars, idx);

        var result = _engine.Analyze(bars, idx, ind, vol);

        Assert.True(result.IsStrongSupport, $"应识别为强承接，Summary={result.Summary}");
        Assert.True(result.ChipLockScore >= 70, $"筹码锁定应>=70，实际={result.ChipLockScore}");
        Assert.True(result.SupportStrength >= 70, $"承接强度应>=70，实际={result.SupportStrength}");
    }

    [Fact]
    public void TestB_DistributionSuspected()
    {
        // 放量下跌+长上影+跌破平台
        var bars = Bars.WithMainUp(40);
        decimal base_ = bars[^1].Close;
        for (int i = 0; i < 5; i++)
            bars.Add(new StockBar { Code = "000001", Name = "测试", Date = DateTime.Today.AddDays(i - 4),
                Open = base_, High = base_ * 1.05m, Low = base_ * 0.92m,
                Close = base_ * 0.93m, Volume = 80000, Amount = base_ * 80000 });

        int idx = bars.Count - 1;
        var vol = _volEngine.Analyze(bars, idx);
        var ind = _calc.Calculate(bars, idx);

        var result = _engine.Analyze(bars, idx, ind, vol);

        Assert.True(result.IsDistributionSuspected, $"应识别为派发嫌疑，Summary={result.Summary}");
        Assert.True(result.ChipLockScore < 50, $"筹码锁定应<50，实际={result.ChipLockScore}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 五、DecisionEngine 集成测试
// ─────────────────────────────────────────────────────────────────────────────
public class DecisionEngineTests
{
    private readonly DecisionEngine _engine = new(new RiskConfig());

    private static MainUpPlatformResult Platform(bool isPlat, decimal lockStr, decimal secondWave) =>
        new(isPlat, 5, 12m, 10m, 0.05m, 0.35m, 0.6m, lockStr, secondWave, 70m, [], "测试平台");

    private static ChipControlResult Chip(decimal chipLock, decimal support) =>
        new(support, chipLock, 70m, support >= 70, chipLock >= 70, false, [], "测试筹码");

    private static SectorEmotionResult Sector(SectorEmotionCycle cycle) =>
        new("测试板块", cycle, 70m, 2, 0.7m, 0.05m, 30m, [], "测试板块情绪");

    private static CycleResult Cycle(MarketCycle c) => new(c, "测试周期");

    private static VolumeResult Vol() => new(VolumeState.ShrinkConsolidate, true, "缩量整理");

    [Fact]
    public void TestA_MainUpPlatform_StrongChip_Consensus()
    {
        var (decision, reason) = _engine.DecideEntry(
            BuySignalType.VolumeBreakout, riskScore: 25, Trend.Up,
            aboveWatchPrice: true, Cycle(MarketCycle.Consensus), Vol(),
            belowMA20: false, macdDead: false, belowStopLoss: false,
            platform: Platform(true, 75, 70),
            chipControl: Chip(75, 75),
            sectorEmotion: Sector(SectorEmotionCycle.Consensus));

        Assert.True(decision == Decision.Buy || decision == Decision.TryBuy,
            $"主升平台+强承接+板块一致，决策应至少为TryBuy，实际={decision}，原因={reason}");
    }

    [Fact]
    public void TestB_MainUpPlatform_SectorDecline()
    {
        var (decision, reason) = _engine.DecideEntry(
            BuySignalType.VolumeBreakout, riskScore: 25, Trend.Up,
            aboveWatchPrice: true, Cycle(MarketCycle.Consensus), Vol(),
            belowMA20: false, macdDead: false, belowStopLoss: false,
            platform: Platform(true, 80, 75),
            sectorEmotion: Sector(SectorEmotionCycle.Decline));

        Assert.True(decision == Decision.Watch || decision == Decision.Ignore,
            $"板块退潮时不允许Buy，实际={decision}");
        Assert.NotNull(reason);
        Assert.True(reason!.Contains("退潮") || reason.Contains("衰退"),
            $"reason应包含退潮或衰退，实际={reason}");
    }

    [Fact]
    public void TestC_OneDayTour()
    {
        var dtResult = new DragonTigerBehaviorResult(
            true, 1, 1, -5000_0000, -0.8m, 0, 0.9m,
            false, false, false, true, false, 30m, 10m, ["一日游风险"], "龙虎榜一日游风险");

        var (decision, reason) = _engine.DecideEntry(
            BuySignalType.VolumeBreakout, riskScore: 30, Trend.Up,
            aboveWatchPrice: true, Cycle(MarketCycle.Consensus), Vol(),
            belowMA20: false, macdDead: false, belowStopLoss: false,
            dragonTiger: dtResult);

        Assert.True(decision == Decision.Watch || decision == Decision.Ignore,
            $"一日游应降级，实际={decision}");
        Assert.NotNull(reason);
        Assert.Contains("一日游", reason);
    }
}
