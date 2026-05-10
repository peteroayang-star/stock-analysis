using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>股票分析主入口，只负责流程编排，不写具体规则</summary>
public class StockAnalyzer
{
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

    public string? LastExcludeReason { get; private set; }

    public StockAnalyzer(AppConfig cfg)
    {
        _signal = new BuySignalDetector(cfg.Signal);
        _decision = new DecisionEngine(cfg.Risk);
        _filter = new StockFilter(cfg.Filter);
    }

    public List<StockSignal> Analyze(List<StockBar> bars, TradingMode mode = TradingMode.Candidate, List<StockBar>? marketBars = null, List<MinuteBar>? minuteBars = null)
    {
        int last = bars.Count - 1;
        if (last < 28) return [];

        var bar = bars[last];
        LastExcludeReason = null;
        var excludeReason = _filter.ShouldExclude(bar.Code, bar.Name, bars);
        if (excludeReason != null) { LastExcludeReason = excludeReason; return []; }

        // 1. 指标
        var ind = _calc.Calculate(bars, last);

        // 2. 大盘弱势
        bool marketWeak = false;
        if (marketBars != null && marketBars.Count >= 20)
        {
            var mInd = _calc.Calculate(marketBars, marketBars.Count - 1);
            marketWeak = mInd != null && mInd.MA5 < mInd.MA20;
        }

        // 3. 量价 → 周期 → 信号 → 风险 → 主力行为
        var vol = _volume.Analyze(bars, last);
        var cycle = _cycle.Detect(bars, last, vol);
        var (signalType, signalReason) = _signal.Detect(bars, last, vol, cycle);
        var riskResult = _risk.Score(bars, last, vol, marketWeak);
        var riskScore = riskResult.Total;
        var riskReasons = riskResult.Reasons;
        var smartMoney = _smartMoney.Analyze(bars, last, vol);
        var intraday = _intraday.Analyze(bars, last, minuteBars);

        // 4. 趋势
        var trend = ind != null && ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20 ? Trend.Up
                  : ind != null && ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20 ? Trend.Down
                  : Trend.Sideways;

        var trendStage = CalcTrendStage(bars, last, ind);

        // 5. 强制规则所需标志
        bool belowMA20 = ind != null && bar.Close < ind.MA20;
        bool macdDead = riskReasons.Any(r => r.Contains("MACD死叉"));
        decimal stopLoss = Math.Round(bar.Close * 0.98m, 2);  // 当前价-2%作为止损
        bool belowStopLoss = stopLoss > 0 && bar.Close <= stopLoss;

        // 6. 决策
        Decision dec;
        string? forceReason;
        if (mode == TradingMode.Portfolio)
            dec = _decision.DecideHolding(riskScore, cycle, belowStopLoss);
        else
            (dec, forceReason) = _decision.DecideEntry(signalType, riskScore, trend,
                ind != null && bar.Close >= ind.MA10 * 1.02m,
                cycle, vol, belowMA20, macdDead, belowStopLoss);

        // 7. 稳定性过滤
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

        // 8. 合并原因（最多3条）
        var reasons = new List<string>();
        if (signalReason != "") reasons.Add(signalReason);
        reasons.AddRange(riskReasons);
        if (mode == TradingMode.Portfolio && dec == Decision.Sell && cycle.Cycle == MarketCycle.End)
            reasons.Insert(0, cycle.Description);

        // 9. 目标价（基于当前价格，空仓时清除）
        decimal? targetPrice = (dec == Decision.Ignore || dec == Decision.Sell)
            ? null : Math.Round(bar.Close * 1.05m, 2);  // 当前价+5%作为目标

        // 10. 辅助标记
        bool supportBroken = belowMA20;
        bool structureAbnormal = supportBroken && (ind == null || bar.Close < ind.MA20 * 0.95m);

        // 11. 14天涨停次数（需在情绪龙头判断前计算）
        int limitUpCount = 0;
        int checkDays = Math.Min(14, last);
        for (int i = last - checkDays + 1; i <= last; i++)
            if (bars[i - 1].Close > 0 && (bars[i].High - bars[i - 1].Close) / bars[i - 1].Close >= 0.095m)
                limitUpCount++;

        // 12. 操作建议
        bool isEmotionLeader = limitUpCount >= 2
            || smartMoney.Behavior == SmartMoneyBehavior.AggressiveAttack
            || (smartMoney.Behavior == SmartMoneyBehavior.HighShock && riskResult.SentimentRisk >= 50);
        var (action, position) = GenerateAdvice(dec, signalType, riskScore, trend, cycle, vol, isEmotionLeader, intraday.Grade);

        // 13. 信号强度
        string strength = (belowMA20 && macdDead) || cycle.Cycle == MarketCycle.End ? "弱"
                        : dec == Decision.Buy ? "强"
                        : dec == Decision.Watch || dec == Decision.TryBuy ? "中"
                        : "弱";

        // 14. 交易价值评分（独立于风险分，越高越值得参与）
        int tradeValue = cycle.Cycle switch {
            MarketCycle.MainUp    => 35,
            MarketCycle.Consensus => 25,
            MarketCycle.Diverge   => 15,
            MarketCycle.Launch    => 10,
            _                     => 0
        };
        tradeValue += vol.State switch {
            VolumeState.AggressiveBuy     => 25,
            VolumeState.ShrinkConsolidate => 15,
            VolumeState.ShrinkPullback    => 10,
            _                             => 0
        };
        tradeValue += signalType switch {
            BuySignalType.VolumeBreakout  => 20,
            BuySignalType.PullbackSupport => 15,
            BuySignalType.TrendPullback   => 12,
            BuySignalType.VolumeWashout   => 10,
            _                             => 0
        };
        if (limitUpCount > 0) tradeValue += Math.Min(limitUpCount * 5, 15);
        if (riskScore >= 66) tradeValue = (int)(tradeValue * 0.4);
        else if (riskScore >= 51) tradeValue = (int)(tradeValue * 0.7);
        tradeValue = Math.Min(tradeValue, 100);

        // 15. 次日涨停潜力评分（分时强度 + 5日线 + 换手率 + 量能结构）
        int limitUpScore = 0;
        limitUpScore += (int)(intraday.Score * 0.4);  // 分时强度权重40%
        if (ind != null && bar.Close > ind.MA5) limitUpScore += 15;  // 站稳5日线
        if (vol.State == VolumeState.AggressiveBuy) limitUpScore += 20;
        else if (vol.State == VolumeState.ShrinkConsolidate) limitUpScore += 10;
        if (limitUpCount > 0) limitUpScore += Math.Min(limitUpCount * 5, 15);  // 连板动能
        if (intraday.Pattern == IntradayPattern.TailTrap || intraday.Pattern == IntradayPattern.SmartExit)
            limitUpScore = (int)(limitUpScore * 0.3);  // 诱多/撤退大幅折扣
        if (riskScore >= 66) limitUpScore = (int)(limitUpScore * 0.4);
        else if (riskScore >= 51) limitUpScore = (int)(limitUpScore * 0.7);
        limitUpScore = Math.Min(limitUpScore, 100);

        return [new StockSignal
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
            AvgVolume10 = ind != null ? (long)ind.VolMA10 : 0,
            SupportBroken = supportBroken,
            StructureAbnormal = structureAbnormal,
            SignalStrength = strength,
            CycleStage = cycle.Description,
            VolumeDescription = vol.Description,
            TradeValueScore = tradeValue,
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
            IntradayDangerZone = intraday.IsDangerZone
        }];
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

    private (string action, int position) GenerateAdvice(
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

        // 半仓条件：倍量突破 + 低风险 + 主升阶段 + 放量进攻
        if (dec == Decision.Buy && signal == BuySignalType.VolumeBreakout &&
            risk <= 20 && cycle.Cycle == MarketCycle.MainUp &&
            vol.State == VolumeState.AggressiveBuy)
            return ("倍量突破确认，主升阶段，量价配合极佳", 50);

        // 40%仓位：倍量突破 + 低风险 + 一致阶段
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
