using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>分时主动资金强度分析</summary>
public record IntradayStrengthResult(
    int Score,
    AttackWill AttackWill,
    IntradayPattern Pattern,
    string Description,
    AttackGrade Grade,
    bool IsDangerZone
);

public class IntradayStrengthEngine
{
    private readonly IndicatorCalculator _calc = new();

    // ── 公开入口 ──────────────────────────────────────────────────────────

    /// <summary>Legacy 兼容（无分时数据，使用日线近似）</summary>
    public IntradayStrengthResult Analyze(List<StockBar> bars, int index)
        => Analyze(bars, index, null, StockStyle.TrendInstitutional);

    /// <summary>主入口：有分时数据时用真实逐分钟统计，否则返回中性结果</summary>
    public IntradayStrengthResult Analyze(List<StockBar> bars, int index, List<MinuteBar>? minuteBars,
        StockStyle style = StockStyle.TrendInstitutional)
    {
        if (minuteBars != null)
            return minuteBars.Count >= 30 ? AnalyzeFromMinute(minuteBars, style) : Neutral();

        return AnalyzeLegacy(bars, index);
    }

    // ── 真实分时分析 ──────────────────────────────────────────────────────

    private static IntradayStrengthResult AnalyzeFromMinute(List<MinuteBar> bars,
        StockStyle style = StockStyle.TrendInstitutional)
    {
        int n = bars.Count;

        // 1. VWAP + 黄线结构
        var vwap = new decimal[n];
        CalculateVwap(bars, vwap);

        decimal aboveRatio = 0;
        for (int i = 0; i < n; i++) if (bars[i].Close > vwap[i]) aboveRatio++;
        aboveRatio /= n;

        bool structureStrong  = aboveRatio >= 0.65m;
        bool structureNeutral = aboveRatio >= 0.45m && aboveRatio < 0.65m;
        bool structureWeak    = aboveRatio < 0.45m;

        int slopeRef = Math.Max(0, n - 30);
        bool avgRising = vwap[n - 1] > vwap[slopeRef] * 1.001m;
        bool avgFlat   = Math.Abs(vwap[n - 1] - vwap[slopeRef]) / (vwap[slopeRef] + 0.001m) < 0.002m;

        // 2. 高低点结构
        var (sessionHigh, sessionLow, risingStructure, afternoonHighBreakCount)
            = AnalyzeHighLowStructure(bars);

        // 3. 上午 vs 下午
        var (morningRatio, afternoonRatio, afternoonStronger, afternoonNewHigh,
            afternoonHigherCenter, morningHigh, afternoonHigh)
            = AnalyzeMorningAfternoon(bars, vwap);

        // 4. 尾盘分析
        var tail = bars.TakeLast(30).ToList();
        var (tailRally, tailTrap, highShock, tailNoJump, tailClosePos, tailBigVol, afternoonBigVol)
            = AnalyzeTail(tail, bars, n, sessionHigh, sessionLow, aboveRatio, structureWeak);

        // 5. 派生指标
        bool morningHighFailed = morningHigh > afternoonHigh * 1.005m
                                 && afternoonRatio < 0.5m
                                 && morningRatio > afternoonRatio + 0.1m;

        bool tailFallBack = tail[0].Open > 0 && tail[^1].Close < tail[0].Open * 0.995m;

        decimal afternoonSlope = CalculateAfternoonVwapSlope(bars, vwap);

        // 6. 阶梯式推升检测
        bool isStepwiseUp = DetectStepwiseUp(avgRising, risingStructure, afternoonStronger,
            aboveRatio, tailClosePos, afternoonNewHigh, afternoonBigVol, highShock,
            tailRally, tailNoJump, afternoonHigherCenter, afternoonHighBreakCount,
            tailFallBack, structureWeak, tailTrap, morningHighFailed);

        // 7. 健康洗盘
        bool hasWashout = aboveRatio < 0.65m && aboveRatio >= 0.45m
                          && risingStructure && tailClosePos > 0.6m;
        bool isHealthyWashout = hasWashout && !isStepwiseUp && !tailTrap;

        // 8. 危险覆盖
        bool isDangerZone = tailTrap || (structureWeak && aboveRatio < 0.35m);

        // 9. 评分
        int score = CalculateIntradayScore(structureStrong, structureNeutral, structureWeak,
            avgRising, avgFlat, risingStructure, afternoonStronger, tailTrap, highShock,
            tailClosePos, tailBigVol, isDangerZone, style, aboveRatio, tailNoJump, tailFallBack);

        // 10. 形态 + 等级 + 描述
        var (pattern, grade, will, desc) = BuildResult(score, style, aboveRatio,
            structureStrong, structureNeutral, structureWeak, avgRising, risingStructure,
            afternoonStronger, afternoonRatio, morningRatio, tailTrap, isStepwiseUp,
            isHealthyWashout, tailClosePos, isDangerZone, morningHighFailed,
            afternoonHigherCenter, afternoonNewHigh, afternoonBigVol,
            afternoonHighBreakCount, tailNoJump, tailFallBack);

        return new(score, will, pattern, desc, grade, isDangerZone);
    }

    // ── 子方法 ────────────────────────────────────────────────────────────

    /// <summary>计算 VWAP（累计成交额 / 累计成交量）序列</summary>
    private static void CalculateVwap(List<MinuteBar> bars, decimal[] vwap)
    {
        decimal cumAmt = 0, cumVol = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            cumAmt += bars[i].Close * bars[i].Volume;
            cumVol += bars[i].Volume;
            vwap[i] = cumVol > 0 ? cumAmt / cumVol : bars[i].Close;
        }
    }

    /// <summary>分时高低点结构分析：更高高点/更低低点计数、上升结构判断、下午创新高次数</summary>
    private static (decimal sessionHigh, decimal sessionLow, bool risingStructure, int afternoonHighBreakCount)
        AnalyzeHighLowStructure(List<MinuteBar> bars)
    {
        int n = bars.Count;
        decimal sessionHigh = bars[0].High, sessionLow = bars[0].Low;
        int higherHighs = 0, lowerLows = 0;
        for (int i = 1; i < n; i++)
        {
            if (bars[i].High > sessionHigh) { higherHighs++; sessionHigh = bars[i].High; }
            if (bars[i].Low  < sessionLow)  { lowerLows++;   sessionLow  = bars[i].Low;  }
        }
        bool risingStructure = higherHighs > lowerLows;

        // 下午创新高次数
        int afternoonHighBreakCount = 0;
        decimal afRunningHigh = 0;
        foreach (var b in bars)
        {
            if (b.Time.Hour < 13) continue;
            if (afRunningHigh == 0) { afRunningHigh = b.High; continue; }
            if (b.High > afRunningHigh) { afternoonHighBreakCount++; afRunningHigh = b.High; }
        }

        return (sessionHigh, sessionLow, risingStructure, afternoonHighBreakCount);
    }

    /// <summary>上午 vs 下午对比分析</summary>
    private static (decimal morningRatio, decimal afternoonRatio, bool afternoonStronger,
        bool afternoonNewHigh, bool afternoonHigherCenter, decimal morningHigh, decimal afternoonHigh)
        AnalyzeMorningAfternoon(List<MinuteBar> bars, decimal[] vwap)
    {
        int n = bars.Count;

        var morning   = bars.Where(b => b.Time.Hour < 13).ToList();
        var afternoon = bars.Where(b => b.Time.Hour >= 13).ToList();

        // 上午黄线上方占比
        int aboveCnt = 0, morningCnt = 0;
        for (int i = 0; i < n && bars[i].Time.Hour < 13; i++)
        { morningCnt++; if (bars[i].Close > vwap[i]) aboveCnt++; }
        decimal morningRatio = morningCnt > 0 ? (decimal)aboveCnt / morningCnt : 0.5m;

        // 下午黄线上方占比
        aboveCnt = 0; int afternoonCnt = 0;
        for (int i = 0; i < n; i++)
            if (bars[i].Time.Hour >= 13) { afternoonCnt++; if (bars[i].Close > vwap[i]) aboveCnt++; }
        decimal afternoonRatio = afternoonCnt > 0 ? (decimal)aboveCnt / afternoonCnt : 0.5m;

        bool afternoonStronger = afternoonRatio > morningRatio + 0.05m;

        decimal afternoonHigh = afternoon.Count > 0 ? afternoon.Max(b => b.High) : 0;
        decimal morningHigh   = morning.Count   > 0 ? morning.Max(b => b.High)   : 0;
        bool afternoonNewHigh = afternoonHigh > morningHigh;

        decimal afternoonAvgClose = afternoon.Count > 0 ? afternoon.Average(b => b.Close) : 0;
        decimal morningAvgClose   = morning.Count   > 0 ? morning.Average(b => b.Close)   : 0;
        bool afternoonHigherCenter = afternoonAvgClose > morningAvgClose * 1.002m;

        return (morningRatio, afternoonRatio, afternoonStronger,
            afternoonNewHigh, afternoonHigherCenter, morningHigh, afternoonHigh);
    }

    /// <summary>尾盘30分钟分析：急拉/诱多/横盘/跳水/量能</summary>
    private static (bool tailRally, bool tailTrap, bool highShock, bool tailNoJump,
        decimal tailClosePos, bool tailBigVol, bool afternoonBigVol)
        AnalyzeTail(List<MinuteBar> tail, List<MinuteBar> bars, int n,
            decimal sessionHigh, decimal sessionLow, decimal aboveRatio, bool structureWeak)
    {
        decimal tailOpen  = tail[0].Open;
        decimal tailClose = tail[^1].Close;
        decimal fullRange = sessionHigh - sessionLow;
        decimal tailClosePos = fullRange > 0 ? (tailClose - sessionLow) / fullRange : 0.5m;

        bool tailRally  = tailOpen > 0 && (tailClose - tailOpen) / tailOpen > 0.015m;
        bool tailTrap   = tailRally && structureWeak && aboveRatio < 0.40m
                          && (tailClose - tailOpen) / tailOpen > 0.025m;
        bool highShock  = tailClosePos > 0.75m && tailOpen > 0
                          && Math.Abs(tailClose - tailOpen) / tailOpen < 0.005m;
        bool tailNoJump = tailOpen > 0 && (tailClose - tailOpen) / tailOpen > -0.005m;

        // 尾盘量能
        long totalVol = bars.Sum(b => b.Volume);
        long tailVol  = tail.Sum(b => b.Volume);
        bool tailBigVol = n > 30 && tailVol > (totalVol - tailVol) / (n - 30) * 30 * 1.5m;

        // 下午量能
        var afternoon = bars.Where(b => b.Time.Hour >= 13).ToList();
        var morning   = bars.Where(b => b.Time.Hour < 13).ToList();
        long afVol = afternoon.Sum(b => b.Volume);
        long amVol = morning.Sum(b => b.Volume);
        bool afternoonBigVol = afternoon.Count > 0 && amVol > 0 && afVol > amVol * 1.2m;

        return (tailRally, tailTrap, highShock, tailNoJump, tailClosePos, tailBigVol, afternoonBigVol);
    }

    /// <summary>下午 VWAP 斜率</summary>
    private static decimal CalculateAfternoonVwapSlope(List<MinuteBar> bars, decimal[] vwap)
    {
        var afVwapBars = bars.Select((b, i) => (b, i)).Where(x => x.b.Time.Hour >= 13).ToList();
        if (afVwapBars.Count < 2) return 0m;
        decimal afFirstVwap = vwap[afVwapBars[0].i];
        decimal afLastVwap  = vwap[afVwapBars[^1].i];
        return afFirstVwap > 0 ? (afLastVwap - afFirstVwap) / afFirstVwap : 0m;
    }

    /// <summary>阶梯式推升检测：9个条件打分，≥7满足视为阶梯推升</summary>
    private static bool DetectStepwiseUp(
        bool avgRising, bool risingStructure, bool afternoonStronger,
        decimal aboveRatio, decimal tailClosePos, bool afternoonNewHigh,
        bool afternoonBigVol, bool highShock, bool tailRally,
        bool tailNoJump, bool afternoonHigherCenter, int afternoonHighBreakCount,
        bool tailFallBack, bool structureWeak, bool tailTrap, bool morningHighFailed)
    {
        int stepCount = 0;
        if (avgRising)             stepCount++;
        if (risingStructure)       stepCount++;
        if (afternoonStronger)     stepCount++;
        if (aboveRatio >= 0.55m)   stepCount++;
        if (tailClosePos >= 0.75m) stepCount++;
        if (afternoonNewHigh)      stepCount++;
        if (afternoonBigVol)       stepCount++;
        if (highShock || (tailClosePos > 0.75m && !tailRally)) stepCount++;
        if (tailNoJump)            stepCount++;
        if (afternoonHigherCenter) stepCount++;

        return stepCount >= 4
            && avgRising
            && afternoonStronger
            && afternoonHighBreakCount >= 2
            && tailClosePos >= 0.75m
            && afternoonHigherCenter
            && !tailFallBack
            && !structureWeak
            && !tailTrap
            && !morningHighFailed;
    }

    /// <summary>计算日内强度评分（0-100），含票型适配</summary>
    private static int CalculateIntradayScore(
        bool structureStrong, bool structureNeutral, bool structureWeak,
        bool avgRising, bool avgFlat, bool risingStructure, bool afternoonStronger,
        bool tailTrap, bool highShock, decimal tailClosePos, bool tailBigVol,
        bool isDangerZone, StockStyle style, decimal aboveRatio,
        bool tailNoJump, bool tailFallBack)
    {
        int score = 0;
        if (structureStrong)       score += 30;
        else if (structureNeutral) score += 15;
        if (avgRising)    score += 20;
        else if (avgFlat) score += 8;
        score += risingStructure ? 15 : 5;
        if (afternoonStronger) score += 10;
        if (!tailTrap && highShock && structureStrong) score += 15;
        else if (!tailTrap && tailClosePos > 0.6m)    score += 10;
        else if (tailTrap)                             score -= 10;
        if (tailBigVol && structureStrong) score += 5;

        if (isDangerZone)       score = Math.Min(score, 40);
        else if (structureWeak) score = Math.Min(score, 60);
        score = Math.Max(0, Math.Min(100, score));

        // 票型适配：趋势机构型/中军容量型用宽松标准
        if (!isDangerZone && style is StockStyle.TrendInstitutional or StockStyle.LargeCapVolume)
        {
            if (aboveRatio >= 0.60m && tailNoJump && risingStructure && !tailFallBack)
                score = Math.Max(score, 62);
            else if (aboveRatio >= 0.55m && tailNoJump && risingStructure)
                score = Math.Max(score, 55);
            else if (structureNeutral && tailNoJump && !tailFallBack)
                score = Math.Max(score, 48);
            else if (structureNeutral && tailNoJump)
                score = Math.Max(score, 42);
        }

        return score;
    }

    /// <summary>形态分类 + 进攻等级 + 意愿 + 描述文字</summary>
    private static (IntradayPattern pattern, AttackGrade grade, AttackWill will, string desc)
        BuildResult(int score, StockStyle style, decimal aboveRatio,
            bool structureStrong, bool structureNeutral, bool structureWeak,
            bool avgRising, bool risingStructure, bool afternoonStronger,
            decimal afternoonRatio, decimal morningRatio, bool tailTrap,
            bool isStepwiseUp, bool isHealthyWashout, decimal tailClosePos,
            bool isDangerZone, bool morningHighFailed, bool afternoonHigherCenter,
            bool afternoonNewHigh, bool afternoonBigVol, int afternoonHighBreakCount,
            bool tailNoJump, bool tailFallBack)
    {
        // 修复震荡判断
        bool isRepairPattern = morningHighFailed || (morningRatio > afternoonRatio + 0.1m && !structureStrong);

        // 趋势震荡偏强
        bool isTrendShock = structureNeutral && risingStructure && !tailTrap
                         && tailClosePos > 0.5m && !morningHighFailed;

        // 形态分类
        IntradayPattern pattern;
        if (tailTrap)
            pattern = IntradayPattern.TailTrap;
        else if (isStepwiseUp)
            pattern = IntradayPattern.StepwiseUp;
        else if (isRepairPattern)
            pattern = IntradayPattern.WeakRecovery;
        else if (structureStrong && avgRising && risingStructure)
            pattern = IntradayPattern.MainUpTrend;
        else if (isHealthyWashout)
            pattern = IntradayPattern.HealthyWashout;
        else if (isTrendShock)
            pattern = IntradayPattern.TrendShock;
        else
            pattern = IntradayPattern.WeakRecovery;

        // 进攻等级
        AttackGrade grade = (tailTrap || isDangerZone) ? AttackGrade.C
            : morningHighFailed ? AttackGrade.B
            : pattern == IntradayPattern.MainUpTrend && tailClosePos > 0.8m ? AttackGrade.S
            : pattern == IntradayPattern.MainUpTrend || pattern == IntradayPattern.StepwiseUp ? AttackGrade.A
            : pattern == IntradayPattern.HealthyWashout || pattern == IntradayPattern.TrendShock ? AttackGrade.B
            : structureNeutral ? AttackGrade.B
            : AttackGrade.C;

        // 进攻意愿
        AttackWill will = score >= 65 ? AttackWill.Strong : score >= 40 ? AttackWill.Medium : AttackWill.Weak;

        // 描述文字
        string pct = $"{aboveRatio:P0}";
        string desc = pattern switch
        {
            IntradayPattern.MainUpTrend    => $"主升趋势，{pct}时间在均线上方，资金主动进攻",
            IntradayPattern.StepwiseUp     => $"阶梯式推升，下午{'强' + (afternoonStronger ? "于上午" : "势延续")}，控节奏推升",
            IntradayPattern.HealthyWashout => $"健康洗盘，{pct}时间在均线上方，回踩后快速修复",
            IntradayPattern.TrendShock     => $"趋势震荡偏强，{pct}时间在均线上方，重心抬高，短线分歧但趋势未坏",
            IntradayPattern.WeakRecovery   => structureNeutral
                ? $"震荡整理，{pct}时间在均线上方，短线分歧但结构中性"
                : $"弱势结构，仅{pct}时间在均线上方",
            IntradayPattern.TailTrap       => $"尾盘急拉诱多，全天弱势（{pct}在均线上方）",
            _                              => ""
        };

        return (pattern, grade, will, desc);
    }

    // ── 无分时数据时的中性结果 ────────────────────────────────────────────

    private static IntradayStrengthResult Neutral() =>
        new(0, AttackWill.Weak, IntradayPattern.WeakRecovery,
            "分时数据不足，无法判断盘口结构", AttackGrade.B, false);

    // ── Legacy：日线 OHLCV 近似（仅用于无分时数据的历史回测）────────────

    private IntradayStrengthResult AnalyzeLegacy(List<StockBar> bars, int index)
    {
        var ind = _calc.Calculate(bars, index);
        if (ind == null) return new(0, AttackWill.Weak, IntradayPattern.WeakRecovery, "数据不足", AttackGrade.C, false);

        var bar  = bars[index];
        var prev = bars[index - 1];
        decimal range    = bar.High - bar.Low;
        decimal closePos = range > 0 ? (bar.Close - bar.Low) / range : 0.5m;
        decimal openPos  = range > 0 ? (bar.Open  - bar.Low) / range : 0.5m;

        var prevInd = index >= 1 ? _calc.Calculate(bars, index - 1) : null;
        bool ma5Rising = prevInd != null && ind.MA5 > prevInd.MA5;
        bool ma5Flat   = prevInd != null && Math.Abs(ind.MA5 - prevInd.MA5) / (prevInd.MA5 + 0.001m) < 0.002m;
        bool ma5Weak   = !ma5Rising;

        bool openAbovePrev  = bar.Open  >= prev.Close;
        bool closeAboveOpen = bar.Close >= bar.Open;

        decimal aboveAvgRatio;
        if      (openAbovePrev  && closeAboveOpen)  aboveAvgRatio = 0.75m;
        else if (openAbovePrev  && !closeAboveOpen) aboveAvgRatio = 0.50m;
        else if (!openAbovePrev && closeAboveOpen)  aboveAvgRatio = 0.45m;
        else                                        aboveAvgRatio = 0.25m;

        bool tailPullUp = openPos < 0.35m && closePos >= 0.45m && closePos < 0.75m;
        if (tailPullUp && aboveAvgRatio < 0.45m) aboveAvgRatio = 0.40m;

        bool structureStrong  = aboveAvgRatio >= 0.65m;
        bool structureNeutral = aboveAvgRatio >= 0.45m && aboveAvgRatio < 0.65m;
        bool structureWeak    = aboveAvgRatio < 0.45m;

        bool higherHigh = index >= 2 && bar.High > bars[index - 1].High;
        bool higherLow  = index >= 2 && bar.Low  > bars[index - 1].Low;
        bool lowerHigh  = index >= 2 && bar.High < bars[index - 1].High;
        bool lowerLow   = index >= 2 && bar.Low  < bars[index - 1].Low;

        decimal vol    = (decimal)bar.Volume;
        bool bigVol    = vol >= ind.VolMA5 * 1.5m;
        bool shrinkVol = vol < ind.VolMA5 * 0.8m;

        decimal lowerShadow  = range > 0 ? (Math.Min(bar.Open, bar.Close) - bar.Low) / range : 0m;
        bool quickRecovery   = lowerShadow > 0.3m && closePos > 0.6m;

        int dangerCount = 0;
        if (structureWeak || aboveAvgRatio < 0.50m)                          dangerCount++;
        if (ma5Weak)                                                          dangerCount++;
        if (openAbovePrev && !closeAboveOpen && closePos < 0.5m)             dangerCount++;
        if (tailPullUp)                                                       dangerCount++;
        if (bigVol && ind.ChangeRate < 1m && closePos < 0.75m)               dangerCount++;
        if (lowerHigh)                                                        dangerCount++;
        if (!openAbovePrev && !closeAboveOpen)                                dangerCount++;

        bool tailTrap = dangerCount >= 3 && !structureStrong;
        bool isDangerZone = tailTrap || structureWeak
            || (ma5Weak && aboveAvgRatio < 0.50m)
            || (tailPullUp && lowerHigh);

        int score = 0;
        if (structureStrong)       score += 30;
        else if (structureNeutral) score += 15;
        if (ma5Rising)    score += 20;
        else if (ma5Flat) score += 8;
        if (higherHigh && higherLow)      score += 20;
        else if (higherHigh || higherLow) score += 10;
        else if (lowerHigh && lowerLow)   score += 0;
        else                              score += 5;
        if (quickRecovery)                               score += 15;
        else if (lowerShadow > 0.2m && closePos > 0.5m) score += 8;
        if (!tailTrap && closePos > 0.8m && bigVol)     score += 15;
        else if (!tailTrap && closePos > 0.6m)          score += 8;
        else if (tailTrap)                              score -= 10;
        if (bigVol && ind.ChangeRate > 1m && closePos > 0.7m) score += 10;
        else if (bigVol && ind.ChangeRate < 0 && lowerLow)    score -= 8;
        else if (shrinkVol && Math.Abs(ind.ChangeRate) < 1m)  score += 5;

        if (isDangerZone)       score = Math.Min(score, 40);
        else if (structureWeak) score = Math.Min(score, 60);
        score = Math.Max(0, Math.Min(100, score));

        bool legacyStepwise = !openAbovePrev && closeAboveOpen && closePos > 0.75m && ma5Rising && !tailTrap;

        IntradayPattern pattern;
        if (tailTrap)
            pattern = IntradayPattern.TailTrap;
        else if (bigVol && ind.ChangeRate < 0 && lowerLow && structureWeak)
            pattern = IntradayPattern.SmartExit;
        else if (structureStrong && ma5Rising && (higherHigh || closePos > 0.8m))
            pattern = IntradayPattern.MainUpTrend;
        else if (legacyStepwise)
            pattern = IntradayPattern.StepwiseUp;
        else if (score < 40 || structureWeak)
            pattern = IntradayPattern.WeakRecovery;
        else
            pattern = IntradayPattern.HealthyWashout;

        AttackGrade grade = (tailTrap || isDangerZone) ? AttackGrade.C
            : structureStrong && ma5Rising && (higherHigh && higherLow) && closePos > 0.8m ? AttackGrade.S
            : (structureStrong && ma5Rising && closePos > 0.65m) || pattern == IntradayPattern.StepwiseUp ? AttackGrade.A
            : structureNeutral || (structureWeak && quickRecovery)                         ? AttackGrade.B
            : AttackGrade.C;

        AttackWill will = score >= 65 ? AttackWill.Strong : score >= 40 ? AttackWill.Medium : AttackWill.Weak;

        string desc = pattern switch
        {
            IntradayPattern.MainUpTrend    => "主升趋势，资金主动进攻",
            IntradayPattern.StepwiseUp     => "阶梯式推升，低开后持续拉升，控节奏推升",
            IntradayPattern.HealthyWashout => "健康洗盘，主力控盘良好",
            IntradayPattern.WeakRecovery   => "弱势修复，资金合力不足",
            IntradayPattern.TailTrap       => "尾盘诱多，全天弱势尾盘拉升",
            IntradayPattern.SmartExit      => "主力撤退，放量下跌结构恶化",
            _                              => ""
        };

        return new(score, will, pattern, desc, grade, isDangerZone);
    }
}
