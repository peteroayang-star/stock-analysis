using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>主力行为识别引擎：先识别市场阶段，再联动计算所有评分，最后做一致性修正</summary>
public class MainForceBehaviorEngine
{
    public MainForceBehaviorResult Analyze(List<StockBar> bars, int index,
        bool intradayWeak = false, bool intradayDanger = false)
    {
        if (index < 20) return Empty();

        var bar    = bars[index];
        var recent = bars.Skip(index - 19).Take(20).ToList();

        decimal ma5    = recent.TakeLast(5).Average(b => b.Close);
        decimal ma10   = recent.TakeLast(10).Average(b => b.Close);
        decimal ma20   = recent.Average(b => b.Close);
        decimal volMa5  = (decimal)recent.TakeLast(5).Average(b => b.Volume);
        decimal volMa10 = (decimal)recent.TakeLast(10).Average(b => b.Volume);

        // ── 基础信号采集 ──────────────────────────────────────
        bool trendUp      = ma5 > ma10 && ma10 > ma20;
        bool trendDown    = ma5 < ma10 && ma10 < ma20;
        bool aboveMA20    = bar.Close >= ma20;
        bool aboveMA5     = bar.Close >= ma5;

        // 缩量抗跌
        bool shrinkResist = false;
        if (index >= 5)
        {
            var seg = bars.Skip(index - 4).Take(5).ToList();
            shrinkResist = seg[4].Volume < seg[0].Volume * 0.85m
                        && seg[4].Low >= seg[0].Low * 0.98m
                        && AmpShrinking(seg);
        }

        // 假跌破修复
        bool falseBrk = false;
        if (index >= 2)
        {
            var b1 = bars[index - 1];
            falseBrk = (b1.Low < ma5 || b1.Low < ma10)
                    && bar.Close >= ma5 * 0.998m
                    && b1.Volume < volMa5 * 1.5m
                    && !(bars[index - 2].Close < bars[index - 2].Open
                         && b1.Close < b1.Open && bar.Close < bar.Open);
        }

        // 平台锁筹
        bool platformLock = false;
        if (index >= 15)
        {
            var seg = bars.Skip(index - 14).Take(15).ToList();
            platformLock = seg.Take(5).Max(b => b.Close) < seg.Skip(5).Max(b => b.Close)
                        && seg.TakeLast(5).Average(b => b.Volume) < seg.Take(5).Average(b => b.Volume) * 0.8
                        && seg.TakeLast(5).Min(b => b.Low) >= seg.Take(5).Min(b => b.Low) * 0.99m
                        && seg.TakeLast(5).All(b => b.Close >= ma20 * 0.97m)
                        && AmpShrinking(seg.TakeLast(5).ToList());
        }

        // 试盘异动
        bool launchTest = false;
        if (index >= 10)
        {
            var seg = bars.Skip(index - 9).Take(10).ToList();
            launchTest = seg.Take(5).Any(b =>
                             b.Close > b.Open && (b.Close - b.Open) / b.Open > 0.05m
                             && b.Volume > (decimal)seg.Take(5).Average(x => x.Volume) * 1.5m)
                      && seg.TakeLast(5).All(b => b.Close >= seg[0].Close * 0.95m)
                      && seg.TakeLast(5).Average(b => b.Volume) < seg.Take(5).Average(b => b.Volume) * 0.85;
        }

        // 趋势加速
        bool accel = false;
        if (index >= 5)
        {
            var seg = bars.Skip(index - 4).Take(5).ToList();
            accel = seg.Count(b => b.Close > b.Open) >= 3
                 && ma5 > ma10
                 && bar.Volume > volMa10 * 1.3m
                 && bar.Close > recent.SkipLast(1).Max(b => b.High);
        }

        // 出货信号
        decimal range       = bar.High - bar.Low;
        decimal upperShadow = range > 0 ? (bar.High - Math.Max(bar.Open, bar.Close)) / range : 0;
        bool volStall    = bar.Volume > volMa10 * 1.5m && Math.Abs(bar.Close - bar.Open) / bar.Open < 0.01m;
        bool longUpperShadow = upperShadow > 0.5m && bar.Volume > volMa5 * 1.2m;
        bool breakPlatform = false;
        if (index >= 3)
        {
            var seg3 = bars.Skip(index - 2).Take(3).ToList();
            breakPlatform = seg3.All(b => b.Close < b.Open) && bar.Close < ma20 * 0.97m;
        }
        bool bigNegBreak = bar.Close < bar.Open && (bar.Open - bar.Close) / bar.Open > 0.03m
                        && bar.Volume > volMa10 * 1.5m && bar.Close < ma10;
        bool weakRebound = index >= 2 && bar.Close > bars[index - 1].Close
                        && bar.Volume < volMa5 * 0.7m && bars[index - 1].Close < bars[index - 2].Close;

        int distSignals = (volStall?1:0) + (longUpperShadow?1:0) + (breakPlatform?1:0)
                        + (bigNegBreak?1:0) + (weakRebound?1:0);
        int bullSignals  = (shrinkResist?1:0) + (falseBrk?1:0) + (platformLock?1:0)
                        + (launchTest?1:0) + (trendUp?1:0);

        // ── 一、市场阶段识别 ──────────────────────────────────
        var stage = DetectStage(trendUp, trendDown, aboveMA20, aboveMA5,
            shrinkResist, falseBrk, platformLock, launchTest, accel,
            distSignals, bullSignals, volStall, bigNegBreak, breakPlatform);

        // ── 二、围绕阶段设定基础分 ────────────────────────────
        var (wash, dist, breakout, health, control, lockup) = StageBaseline(stage);
        var tags = new List<string>();

        // ── 三、信号叠加（增量，不再是全部来源）─────────────
        if (shrinkResist)  { wash += 15; lockup += 10; health += 10; tags.Add("缩量抗跌"); tags.Add("筹码锁定"); }
        if (falseBrk)      { wash += 15; control += 15; dist -= 10;  tags.Add("假跌破洗盘"); tags.Add("快速修复"); tags.Add("主力控盘"); }
        if (platformLock)  { lockup += 15; breakout += 15; health += 10; tags.Add("平台锁筹"); tags.Add("重心抬高"); }
        if (launchTest)    { breakout += 10; control += 10; tags.Add("主力试盘"); tags.Add("异动吸筹"); }
        if (accel)         { breakout += 15; health += 10; control += 10; tags.Add("主升启动"); tags.Add("趋势加速"); tags.Add("资金合力"); }
        if (volStall)      { dist += 15; health -= 10; tags.Add("放量滞涨"); }
        if (longUpperShadow){ dist += 10; tags.Add("冲高回落"); }
        if (breakPlatform) { dist += 15; health -= 15; lockup -= 10; tags.Add("跌破平台"); }
        if (bigNegBreak)   { dist += 15; health -= 15; tags.Add("放量长阴破位"); }
        if (weakRebound)   { dist += 8;  tags.Add("反抽无量"); }
        if (trendUp && aboveMA5) { health += 10; breakout += 5; }

        // ── 四、行为联动修正 ──────────────────────────────────
        // 多个牛市信号共振 → 提升洗盘/主升/健康，压制出货
        if (bullSignals >= 3)
        {
            wash     += 15; breakout += 15; health += 10;
            dist      = (int)(dist * 0.6);
            tags.Add("多信号共振");
        }
        // 出货信号主导 → 压制洗盘/主升
        if (distSignals >= 2)
        {
            wash     = (int)(wash * 0.5);
            breakout = (int)(breakout * 0.5);
            health   = (int)(health * 0.7);
            lockup   = (int)(lockup * 0.7);
        }

        // ── 五、逻辑一致性检查 ────────────────────────────────
        // 主力控盘高但主升预备极低 → 修正
        if (control >= 50 && breakout < 25)
            breakout = 25;
        // 趋势向上但趋势健康度极低 → 修正
        if (trendUp && health < 30)
            health = 30;
        // 假跌破修复但洗盘概率极低 → 修正
        if (falseBrk && wash < 25)
            wash = 25;
        // 分歧洗筹期：出货不能高
        if (stage == MarketStage.WashoutDip && dist > 30)
            dist = 30;
        // 高位派发期：健康度/锁定度不能高
        if (stage == MarketStage.TopDistribute || stage == MarketStage.Exhaustion)
        { health = Math.Min(health, 35); lockup = Math.Min(lockup, 35); }

        wash     = Math.Clamp(wash,     0, 100);
        dist     = Math.Clamp(dist,     0, 100);
        breakout = Math.Clamp(breakout, 0, 100);
        health   = Math.Clamp(health,   0, 100);
        control  = Math.Clamp(control,  0, 100);
        lockup   = Math.Clamp(lockup,   0, 100);

        return new MainForceBehaviorResult(wash, dist, breakout, health, control, lockup,
            tags.Distinct().ToList(),
            BuildAnalysis(stage, wash, dist, breakout, health, control, lockup, intradayWeak, intradayDanger),
            stage);
    }

    // ── 市场阶段识别 ─────────────────────────────────────────
    private static MarketStage DetectStage(
        bool trendUp, bool trendDown, bool aboveMA20, bool aboveMA5,
        bool shrinkResist, bool falseBrk, bool platformLock, bool launchTest, bool accel,
        int distSignals, int bullSignals, bool volStall, bool bigNegBreak, bool breakPlatform)
    {
        // 退潮衰竭：趋势向下 + 出货信号多
        if (trendDown && distSignals >= 2)
            return MarketStage.Exhaustion;
        // 高位派发：不在趋势向上 + 出货信号
        if (!trendUp && (volStall || bigNegBreak || breakPlatform) && distSignals >= 1)
            return MarketStage.TopDistribute;
        // 主升加速：趋势向上 + 加速突破
        if (trendUp && accel)
            return MarketStage.MainUpAccel;
        // 趋势中继：趋势向上 + 平台锁筹或缩量抗跌
        if (trendUp && (platformLock || shrinkResist) && bullSignals >= 2)
            return MarketStage.TrendRelay;
        // 分歧洗筹：趋势向上 + 假跌破或缩量抗跌
        if (trendUp && (falseBrk || shrinkResist))
            return MarketStage.WashoutDip;
        // 启动试盘：有试盘异动 + 未破MA20
        if (launchTest && aboveMA20)
            return MarketStage.LaunchTest;
        // 底部吸筹：均线下方 + 无明显出货
        if (!aboveMA20 && distSignals == 0)
            return MarketStage.Accumulation;
        // 默认：趋势向上归中继，否则吸筹
        return trendUp ? MarketStage.TrendRelay : MarketStage.Accumulation;
    }

    // ── 阶段基础分 ───────────────────────────────────────────
    private static (int wash, int dist, int breakout, int health, int control, int lockup)
        StageBaseline(MarketStage stage) => stage switch
    {
        MarketStage.Accumulation   => (20, 10, 15, 35, 25, 30),
        MarketStage.LaunchTest     => (25, 10, 35, 45, 35, 35),
        MarketStage.WashoutDip     => (45, 10, 30, 50, 40, 40),
        MarketStage.TrendRelay     => (35, 10, 40, 55, 45, 45),
        MarketStage.MainUpAccel    => (15, 10, 65, 70, 60, 55),
        MarketStage.TopDistribute  => (10, 45, 10, 30, 20, 25),
        MarketStage.Exhaustion     => (5,  60, 5,  15, 10, 15),
        _                          => (20, 20, 20, 35, 25, 30)
    };

    private static bool AmpShrinking(List<StockBar> seg)
    {
        if (seg.Count < 3) return false;
        var amps = seg.Select(b => b.High - b.Low).ToList();
        return amps.Last() < amps.First() * 0.85m;
    }

    private static string BuildAnalysis(MarketStage stage, int wash, int dist,
        int breakout, int health, int control, int lockup,
        bool intradayWeak, bool intradayDanger)
    {
        // 短弱长强前缀：分时偏弱但趋势结构健康时，主动解释
        string shortWeakPrefix = "";
        bool trendHealthy = stage is MarketStage.WashoutDip or MarketStage.TrendRelay
                                  or MarketStage.LaunchTest or MarketStage.MainUpAccel;
        if (intradayWeak && trendHealthy && !intradayDanger)
            shortWeakPrefix = "【注意】当日分时资金偏弱，但这属于短线级别的正常波动，不代表趋势走坏。日K结构显示：";

        string core = stage switch
        {
            MarketStage.Accumulation =>
                $"当前处于底部吸筹期，均线结构尚未走强，但出货信号不明显，主力可能正在低位建仓。趋势健康度{health}分，筹码锁定度{lockup}分，需等待放量启动信号确认。",
            MarketStage.LaunchTest =>
                $"出现试盘异动特征，主力放量拉升后缩量整理，未出现A杀，筹码正在锁定（锁定度{lockup}分）。主升预备概率{breakout}%，若后续缩量不破启动位，可视为蓄势信号。",
            MarketStage.WashoutDip =>
                $"当前处于分歧洗筹期（洗盘概率{wash}%），趋势向上但出现回踩，假跌破后快速修复，主力控盘强度{control}分。出货概率仅{dist}%，MA20支撑有效，低点持续抬高，属于趋势中的正常洗盘，筹码锁定度{lockup}分，可耐心持有等待突破。",
            MarketStage.TrendRelay =>
                $"当前处于趋势中继期，平台整理特征明显，低点缓慢抬高，筹码锁定度{lockup}分，主力控盘强度{control}分。趋势健康度{health}分，主升预备概率{breakout}%，若放量突破平台高点，可能进入加速段。",
            MarketStage.MainUpAccel =>
                $"主升加速期确认，趋势健康度{health}分，资金合力明显，控盘强度{control}分。主升预备概率{breakout}%，量价配合良好，当前为趋势最强阶段，注意高位放量滞涨信号出现时及时减仓。",
            MarketStage.TopDistribute =>
                $"当前出现高位派发特征（出货概率{dist}%），放量滞涨或连续冲高回落，主力兑现意愿增强。趋势健康度已降至{health}分，筹码锁定度{lockup}分，建议谨慎持仓，关注是否跌破关键支撑位。",
            MarketStage.Exhaustion =>
                $"当前处于退潮衰竭期（出货概率{dist}%），趋势结构已破坏，放量下跌或反抽无量，主力撤退迹象明显。趋势健康度仅{health}分，不建议参与，等待结构重建后再评估。",
            _ =>
                $"量价结构处于博弈阶段，洗盘概率{wash}%，出货概率{dist}%，主升预备概率{breakout}%，趋势健康度{health}分。暂无明确主力行为特征，建议等待更清晰的量价信号。"
        };

        return string.IsNullOrEmpty(shortWeakPrefix) ? core : shortWeakPrefix + core;
    }

    private static MainForceBehaviorResult Empty() =>
        new(0, 0, 0, 0, 0, 0, [], "数据不足，无法进行主力行为分析。", MarketStage.Accumulation);
}
