using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>股票分析主入口，只负责流程编排，不写具体规则</summary>
public class StockAnalyzer
{
    // TODO: 子引擎实例化应通过依赖注入，当前为减少改动范围保留 new
    private readonly BuySignalDetector _signal;
    private readonly RiskScoreEngine _risk = new();
    private readonly DecisionEngine _decision;
    private readonly StockFilter _filter;
    private readonly StockStabilityFilter _stability = new();
    private readonly IndicatorCalculator _calc = new();
    private readonly VolumeEngine _volume = new();
    private readonly CycleDetector _cycle = new();
    private readonly SmartMoneyEngine _smartMoney = new();
    private readonly IntradayStrengthEngine _intraday = new();
    private readonly MainForceBehaviorEngine _mainForce = new();
    private readonly StockStyleDetector _styleDetector = new();
    private readonly MainUpPlatformEngine _mainUpPlatform = new();
    private readonly DragonTigerBehaviorEngine _dragonTiger = new();
    private readonly SectorEmotionEngine _sectorEmotion = new();
    private readonly ChipControlEngine _chipControl = new();
    private readonly SectorResonanceEngine _sectorResonance = new();

    public string? LastExcludeReason { get; private set; }

    public StockAnalyzer(AppConfig cfg)
    {
        _signal = new BuySignalDetector(cfg.Signal);
        _decision = new DecisionEngine(cfg.Risk);
        _filter = new StockFilter(cfg.Filter);
    }

    public List<StockSignal> Analyze(List<StockBar> bars, TradingMode mode = TradingMode.Candidate,
        List<StockBar>? marketBars = null, List<MinuteBar>? minuteBars = null,
        List<DragonTigerRecord>? dragonTigerRecords = null, List<List<StockBar>>? sectorStocks = null,
        string? sectorName = null, bool skipAmountFilter = false)
    {
        int last = bars.Count - 1;
        if (last < 28) return [];

        var bar = bars[last];
        LastExcludeReason = null;
        var excludeReason = _filter.ShouldExclude(bar.Code, bar.Name, bars, skipAmountFilter);
        if (excludeReason != null) { LastExcludeReason = excludeReason; return []; }

        // ── 1. 基础指标 + 大盘 ──────────────────────────────────
        var ind = _calc.Calculate(bars, last);
        bool marketWeak = ComputeMarketWeak(marketBars);

        // ── 2. 量价 → 周期 → 信号 → 主力 → 涨停 → 票型 → 风险 ──
        var vol = _volume.Analyze(bars, last);
        var cycle = _cycle.Detect(bars, last, vol);
        var (signalType, signalReason) = _signal.Detect(bars, last, vol, cycle);
        var smartMoney = _smartMoney.Analyze(bars, last, vol);
        int limitUpCount = ComputeLimitUpCount(bars, last);
        var stockStyle = _styleDetector.Detect(bars, last, limitUpCount);
        var riskResult = _risk.Score(bars, last, vol, marketWeak, stockStyle);
        var riskScore = riskResult.Total;
        var riskReasons = riskResult.Reasons;
        var intraday = _intraday.Analyze(bars, last, minuteBars, stockStyle);
        var mainForce = _mainForce.Analyze(bars, last,
            intradayWeak: intraday.Score < 40,
            intradayDanger: intraday.IsDangerZone);

        // ── 3. 新引擎 ──────────────────────────────────────────
        var platform    = _mainUpPlatform.Analyze(bars, last, ind, vol, cycle, riskScore);
        var dragonTiger = _dragonTiger.Analyze(dragonTigerRecords);
        var sectorEmo   = _sectorEmotion.Analyze(sectorName, sectorStocks);
        var chipControl = _chipControl.Analyze(bars, last, ind, vol);
        var sectorRes   = _sectorResonance.Analyze(bars, sectorStocks, minuteBars);

        // ── 4. 趋势 + 止损 ─────────────────────────────────────
        var trend = ind != null && ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20 ? Trend.Up
                  : ind != null && ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20 ? Trend.Down
                  : Trend.Sideways;
        var trendStage = CalcTrendStage(bars, last, ind);

        bool belowMA20 = ind != null && bar.Close < ind.MA20;
        bool macdDead = riskReasons.Any(r => r.Contains("MACD死叉"));
        // 止损价：模型暂无持仓成本字段，使用前一日收盘作为入场参考价（模拟前一日入场）
        decimal refPrice = last >= 1 ? bars[last - 1].Close : bar.Close;
        decimal stopLoss = Math.Round(refPrice * 0.98m, 2);
        bool belowStopLoss = bar.Close <= stopLoss
            || (ind != null && bar.Close < ind.MA20 && refPrice > ind.MA20);

        // ── 5. 决策 ────────────────────────────────────────────
        Decision dec;
        string? forceReason;
        if (mode == TradingMode.Portfolio)
            dec = _decision.DecideHolding(riskScore, cycle, belowStopLoss);
        else
            (dec, forceReason) = _decision.DecideEntry(signalType, riskScore, trend,
                ind != null && bar.Close >= ind.MA10 * 1.02m,
                cycle, vol, belowMA20, macdDead, belowStopLoss,
                platform, dragonTiger, sectorEmo, chipControl, sectorRes);

        // ── 6. 稳定性过滤 ──────────────────────────────────────
        if (mode == TradingMode.Candidate)
        {
            var (stabilityScore, passRequired) = _stability.Evaluate(bars, last);
            if (!passRequired || stabilityScore < 60)
            {
                if (dec == Decision.Buy || dec == Decision.TryBuy)
                    dec = stabilityScore < 40 ? Decision.Ignore : Decision.Watch;
                riskReasons.Add($"稳定性评分{stabilityScore}，信号降级");
            }
        }

        // ── 7. 组装结果 ────────────────────────────────────────
        var reasons = new List<string>();
        if (signalReason != "") reasons.Add(signalReason);
        reasons.AddRange(riskReasons);
        if (mode == TradingMode.Portfolio && dec == Decision.Sell && cycle.Cycle == MarketCycle.End)
            reasons.Insert(0, cycle.Description);

        decimal? targetPrice = (dec == Decision.Ignore || dec == Decision.Sell)
            ? null : Math.Round(bar.Close * 1.05m, 2);

        bool isEmotionLeader = limitUpCount >= 2
            || smartMoney.Behavior == SmartMoneyBehavior.AggressiveAttack
            || (smartMoney.Behavior == SmartMoneyBehavior.HighShock && riskResult.SentimentRisk >= 50);
        var (action, position) = GenerateAdvice(dec, signalType, riskScore, trend, cycle, vol, isEmotionLeader, intraday.Grade);

        int limitUpScore = ComputeLimitUpScore(intraday, bar, ind, vol, limitUpCount, stockStyle, mainForce, riskScore);

        return [BuildResult(bar, signalType, riskScore, dec, reasons, ind, stopLoss, targetPrice,
            trend, trendStage, action, position, intraday, limitUpCount, belowMA20, cycle, vol,
            smartMoney, riskResult, isEmotionLeader, limitUpScore, mainForce, platform,
            dragonTiger, sectorEmo, chipControl, sectorRes)];
    }

    // ── 子方法 ────────────────────────────────────────────────────────────

    /// <summary>计算14天内涨停次数（唯一计算点，消除重复）</summary>
    private static int ComputeLimitUpCount(List<StockBar> bars, int last)
    {
        int count = 0;
        int checkDays = Math.Min(14, last);
        for (int i = last - checkDays + 1; i <= last; i++)
            if (bars[i - 1].Close > 0 && (bars[i].High - bars[i - 1].Close) / bars[i - 1].Close >= 0.095m)
                count++;
        return count;
    }

    /// <summary>大盘弱势判断</summary>
    private bool ComputeMarketWeak(List<StockBar>? marketBars)
    {
        if (marketBars == null || marketBars.Count < 20) return false;
        var mInd = _calc.Calculate(marketBars, marketBars.Count - 1);
        return mInd != null && mInd.MA5 < mInd.MA20;
    }

    /// <summary>次日涨停潜力评分（分时强度 + 5日线 + 量能结构 + 趋势惯性）</summary>
    private static int ComputeLimitUpScore(IntradayStrengthResult intraday, StockBar bar,
        IndicatorCalculator.Indicators? ind, VolumeResult vol, int limitUpCount,
        StockStyle stockStyle, MainForceBehaviorResult mainForce, int riskScore)
    {
        int score = 0;
        score += (int)(intraday.Score * 0.4);
        if (ind != null && bar.Close > ind.MA5) score += 15;
        if (vol.State == VolumeState.AggressiveBuy) score += 20;
        else if (vol.State == VolumeState.ShrinkConsolidate) score += 10;
        if (limitUpCount > 0) score += Math.Min(limitUpCount * 5, 15);

        if (stockStyle is StockStyle.TrendInstitutional or StockStyle.LargeCapVolume)
        {
            if (mainForce.Stage is MarketStage.MainUpAccel)
                score += 20;
            else if (mainForce.Stage is MarketStage.TrendRelay or MarketStage.WashoutDip)
                score += 12;
        }

        if (intraday.Pattern == IntradayPattern.TailTrap || intraday.Pattern == IntradayPattern.SmartExit)
            score = (int)(score * 0.3);
        if (riskScore >= 66) score = (int)(score * 0.4);
        else if (riskScore >= 51) score = (int)(score * 0.7);
        return Math.Min(score, 100);
    }

    /// <summary>构建 StockSignal 结果对象</summary>
    private static StockSignal BuildResult(StockBar bar, BuySignalType signalType, int riskScore,
        Decision dec, List<string> reasons, IndicatorCalculator.Indicators? ind,
        decimal stopLoss, decimal? targetPrice, Trend trend, TrendStage trendStage,
        string action, int position, IntradayStrengthResult intraday, int limitUpCount,
        bool belowMA20, CycleResult cycle, VolumeResult vol, SmartMoneyResult smartMoney,
        RiskResult riskResult, bool isEmotionLeader, int limitUpScore,
        MainForceBehaviorResult mainForce, MainUpPlatformResult? platform,
        DragonTigerBehaviorResult? dragonTiger, SectorEmotionResult? sectorEmo,
        ChipControlResult? chipControl, SectorResonanceResult? sectorRes)
    {
        return new StockSignal
        {
            Code = bar.Code, Name = bar.Name, Date = bar.Date, Close = bar.Close,
            SignalType = signalType, RiskScore = riskScore, Decision = dec,
            Reasons = reasons.Take(3).ToList(),
            SupportPrice  = ind != null ? Math.Round(ind.MA20, 2) : null,
            StopLossPrice = stopLoss > 0 ? stopLoss : null,
            WatchPrice    = ind != null ? Math.Round(ind.MA10 * 1.02m, 2) : null,
            TargetPrice   = targetPrice,
            Trend = trend, TrendStage = trendStage,
            ActionAdvice = action,
            PositionPct = intraday.IsDangerZone ? 0
                        : intraday.Grade == AttackGrade.B ? Math.Min(position, 15)
                        : position,
            LimitUpCountIn14Days = limitUpCount,
            SupportBroken = belowMA20,
            CycleStage = cycle.Description,
            VolumeDescription = vol.Description,
            SmartMoney = smartMoney.Behavior,
            SmartMoneyDescription = smartMoney.Description,
            TrendRisk = riskResult.TrendRisk,
            VolatilityRisk = riskResult.VolatilityRisk,
            SentimentRisk = riskResult.SentimentRisk,
            IsEmotionLeader = isEmotionLeader,
            IntradayStrengthScore = intraday.Score,
            AttackWillDescription = intraday.AttackWill switch {
                AttackWill.Strong => "强", AttackWill.Medium => "中", _ => "弱"
            },
            IntradayPattern = intraday.Description,
            IntradayPatternType = intraday.Pattern,
            NextDayLimitUpScore = intraday.IsDangerZone ? 0 : limitUpScore,
            AttackGrade = intraday.Grade,
            IntradayDangerZone = intraday.IsDangerZone,
            MainForceBehavior = mainForce,
            MainUpPlatform = platform,
            DragonTiger    = dragonTiger,
            SectorEmotion  = sectorEmo,
            ChipControl    = chipControl,
            SectorResonance = sectorRes,
        };
    }

    private TrendStage CalcTrendStage(List<StockBar> bars, int last, IndicatorCalculator.Indicators? ind)
    {
        if (ind == null) return TrendStage.Sideways;
        if (ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20) return TrendStage.Down;
        if (ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20)
        {
            var prevInd = last >= 20 ? _calc.Calculate(bars, last - 1) : null;
            if (prevInd == null) return TrendStage.EarlyUp;
            if (ind.MA5 > prevInd.MA5 * 1.005m) return TrendStage.MidUp;
            if (bars[last].Close > ind.MA20 * 1.20m) return TrendStage.LateUp;
            return TrendStage.EarlyUp;
        }
        return TrendStage.Sideways;
    }

    private static (string action, int position) GenerateAdvice(
        Decision dec, BuySignalType signal, int risk, Trend trend,
        CycleResult cycle, VolumeResult vol, bool isEmotionLeader, AttackGrade grade = AttackGrade.B)
    {
        if (isEmotionLeader)
        {
            return dec switch
            {
                Decision.Buy or Decision.TryBuy
                    => ("情绪龙头，连板强度与换手需关注，高位博弈风险较高", 15),
                Decision.Watch
                    => ("情绪高位，等待情绪回落或缩量企稳", 0),
                Decision.Hold
                    => ("龙头持有，关注放量滞涨信号", 0),
                Decision.Reduce
                    => ("情绪见顶风险，分歧增强", 0),
                Decision.Sell
                    => ("情绪转弱，趋势结构恶化", 0),
                _ => ("情绪博弈风险极高", 0)
            };
        }

        if (dec == Decision.Buy && signal == BuySignalType.VolumeBreakout &&
            risk <= 20 && cycle.Cycle == MarketCycle.MainUp &&
            vol.State == VolumeState.AggressiveBuy)
            return ("倍量突破确认，主升阶段，量价配合极佳", 50);

        if (dec == Decision.Buy && signal == BuySignalType.VolumeBreakout &&
            risk <= 25 && cycle.Cycle == MarketCycle.Consensus)
            return ("倍量突破确认，量价配合良好", 40);

        return dec switch
        {
            Decision.Buy when signal == BuySignalType.VolumeBreakout
                => ("倍量突破确认，量价配合良好", 35),
            Decision.Buy when signal == BuySignalType.TrendPullback
                => ("上涨趋势仍在，回调未破关键均线", 30),
            Decision.Buy
                => ("信号出现，量价关系健康", 25),
            Decision.TryBuy when trend == Trend.Up && (grade == AttackGrade.S || grade == AttackGrade.A) && risk <= 25
                => ("突破确认，趋势向上，分时强势，可积极建仓", 50),
            Decision.TryBuy when trend == Trend.Up && (grade == AttackGrade.S || grade == AttackGrade.A)
                => ("突破确认，趋势向上，分时结构良好", 30),
            Decision.TryBuy
                => ("价格突破观察位且趋势向上", 15),
            Decision.Watch
                => ("暂时观望，等待更明确信号或风险降低", 0),
            Decision.Hold
                => ("持续观察趋势演化", 0),
            Decision.Reduce
                => ("风险上升，高位分歧明显", 0),
            Decision.Sell
                => ("触发止损位，趋势结构破坏", 0),
            _ => ("无明确信号", 0)
        };
    }
}
