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

        // 1. 黄线 = VWAP（累计成交额 / 累计成交量）
        var vwap = new decimal[n];
        decimal cumAmt = 0, cumVol = 0;
        for (int i = 0; i < n; i++)
        {
            cumAmt += bars[i].Close * bars[i].Volume;
            cumVol += bars[i].Volume;
            vwap[i] = cumVol > 0 ? cumAmt / cumVol : bars[i].Close;
        }

        // 2. 全天黄线上方占比
        int aboveCount = 0;
        for (int i = 0; i < n; i++) if (bars[i].Close > vwap[i]) aboveCount++;
        decimal aboveRatio = (decimal)aboveCount / n;

        bool structureStrong  = aboveRatio >= 0.65m;
        bool structureNeutral = aboveRatio >= 0.45m && aboveRatio < 0.65m;
        bool structureWeak    = aboveRatio < 0.45m;

        // 3. 黄线斜率（全天 vs 最后30分钟）
        int slopeRef = Math.Max(0, n - 30);
        bool avgRising = vwap[n - 1] > vwap[slopeRef] * 1.001m;
        bool avgFlat   = Math.Abs(vwap[n - 1] - vwap[slopeRef]) / (vwap[slopeRef] + 0.001m) < 0.002m;

        // 4. 分时高低点结构
        decimal sessionHigh = bars[0].High, sessionLow = bars[0].Low;
        int higherHighs = 0, lowerLows = 0;
        for (int i = 1; i < n; i++)
        {
            if (bars[i].High > sessionHigh) { higherHighs++; sessionHigh = bars[i].High; }
            if (bars[i].Low  < sessionLow)  { lowerLows++;   sessionLow  = bars[i].Low;  }
        }
        bool risingStructure = higherHighs > lowerLows;

        // 5. 上午 vs 下午（13:00 分割）
        var morning   = bars.Where(b => b.Time.Hour < 13).ToList();
        var afternoon = bars.Where(b => b.Time.Hour >= 13).ToList();

        decimal MorningAbove() {
            if (morning.Count == 0) return 0.5m;
            int idx0 = 0; // vwap index offset
            int above = 0;
            for (int i = 0; i < n && bars[i].Time.Hour < 13; i++)
                if (bars[i].Close > vwap[i]) above++;
            return (decimal)above / morning.Count;
        }
        decimal AfternoonAbove() {
            if (afternoon.Count == 0) return 0.5m;
            int above = 0, total = 0;
            for (int i = 0; i < n; i++)
                if (bars[i].Time.Hour >= 13) { if (bars[i].Close > vwap[i]) above++; total++; }
            return total > 0 ? (decimal)above / total : 0.5m;
        }

        decimal morningRatio   = MorningAbove();
        decimal afternoonRatio = AfternoonAbove();
        bool afternoonStronger = afternoonRatio > morningRatio + 0.05m;

        decimal afternoonHigh = afternoon.Count > 0 ? afternoon.Max(b => b.High) : 0;
        decimal morningHigh   = morning.Count   > 0 ? morning.Max(b => b.High)   : 0;
        bool afternoonNewHigh = afternoonHigh > morningHigh;

        decimal afternoonAvgClose = afternoon.Count > 0 ? afternoon.Average(b => b.Close) : 0;
        decimal morningAvgClose   = morning.Count   > 0 ? morning.Average(b => b.Close)   : 0;
        bool afternoonHigherCenter = afternoonAvgClose > morningAvgClose * 1.002m;

        // 6. 尾盘30分钟
        var tail = bars.TakeLast(30).ToList();
        decimal tailOpen  = tail[0].Open;
        decimal tailClose = tail[^1].Close;
        decimal fullRange = sessionHigh - sessionLow;
        decimal tailClosePos = fullRange > 0 ? (tailClose - sessionLow) / fullRange : 0.5m;

        bool tailRally  = tailOpen > 0 && (tailClose - tailOpen) / tailOpen > 0.015m;
        // 真正诱多：全天弱势 + 尾盘急拉幅度大 + 拉升前长时间压在均线下
        bool tailTrap   = tailRally && structureWeak && aboveRatio < 0.40m
                          && (tailClose - tailOpen) / tailOpen > 0.025m;
        bool highShock  = tailClosePos > 0.75m && tailOpen > 0
                          && Math.Abs(tailClose - tailOpen) / tailOpen < 0.005m;
        bool tailNoJump = tailOpen > 0 && (tailClose - tailOpen) / tailOpen > -0.005m; // 尾盘未跳水

        // 7. 量能
        long totalVol = bars.Sum(b => b.Volume);
        long tailVol  = tail.Sum(b => b.Volume);
        long afVol    = afternoon.Sum(b => b.Volume);
        long amVol    = morning.Sum(b => b.Volume);
        bool tailBigVol      = n > 30 && tailVol > (totalVol - tailVol) / (n - 30) * 30 * 1.5m;
        bool afternoonBigVol = afternoon.Count > 0 && amVol > 0 && afVol > amVol * 1.2m;

        // 8. 新增指标计算
        // 分时低点抬高评分
        int lowPointRisingScore = 0;
        for (int i = 1; i < n; i++)
            if (bars[i].Low > bars[i - 1].Low) lowPointRisingScore++;

        // 分时高点抬高评分
        int highPointRisingScore = 0;
        for (int i = 1; i < n; i++)
            if (bars[i].High > bars[i - 1].High) highPointRisingScore++;

        // 下午创新高次数
        int afternoonHighBreakCount = 0;
        decimal afRunningHigh = 0;
        foreach (var b in afternoon)
        {
            if (afRunningHigh == 0) { afRunningHigh = b.High; continue; }
            if (b.High > afRunningHigh) { afternoonHighBreakCount++; afRunningHigh = b.High; }
        }

        // 下午最低点 > 上午最低点
        decimal morningLow = morning.Count > 0 ? morning.Min(b => b.Low) : 0;
        decimal afternoonLow = afternoon.Count > 0 ? afternoon.Min(b => b.Low) : 0;
        bool afternoonLowHigherThanMorning = morningLow > 0 && afternoonLow > morningLow;

        // 尾盘回落
        bool tailFallBack = tailOpen > 0 && tailClose < tailOpen * 0.995m;

        // 下午VWAP斜率
        decimal afternoonSlope = 0m;
        var afVwapBars = bars.Select((b, i) => (b, i)).Where(x => x.b.Time.Hour >= 13).ToList();
        if (afVwapBars.Count >= 2)
        {
            decimal afFirstVwap = vwap[afVwapBars[0].i];
            decimal afLastVwap  = vwap[afVwapBars[^1].i];
            afternoonSlope = afFirstVwap > 0 ? (afLastVwap - afFirstVwap) / afFirstVwap : 0m;
        }

        // 修复震荡评分
        bool repairScore = morningRatio > afternoonRatio + 0.1m;

        // 上午冲高失败检测
        bool morningHighFailed = morningHigh > afternoonHigh * 1.005m
                                 && afternoonRatio < 0.5m
                                 && morningRatio > afternoonRatio + 0.1m;

        // 阶梯式推升检测
        int stepCount = 0;
        if (avgRising)             stepCount++; // 1. 黄线缓慢上移（核心）
        if (risingStructure)       stepCount++; // 2. 低点持续抬高
        if (afternoonStronger)     stepCount++; // 3. 下午强于上午（核心）
        if (aboveRatio >= 0.55m)   stepCount++; // 4. 大部分时间在黄线上方（放宽到55%）
        if (tailClosePos >= 0.75m) stepCount++; // 5. 收盘位于振幅75%以上
        if (afternoonNewHigh)      stepCount++; // 6. 下午创新高
        if (afternoonBigVol)       stepCount++; // 7. 下午放量
        if (highShock || (tailClosePos > 0.75m && !tailRally)) stepCount++; // 8. 尾盘横住
        if (tailNoJump)            stepCount++; // 9. 尾盘未跳水
        if (afternoonHigherCenter) stepCount++; // 10. 下午价格中枢高于上午

        // A 阶梯推升必须同时满足7个条件
        bool isStepwiseUp = stepCount >= 4
            && avgRising                          // 条件2：黄线上移
            && afternoonStronger                  // 条件1：下午强于上午
            && afternoonHighBreakCount >= 2       // 条件3：下午至少2次新高
            && tailClosePos >= 0.75m              // 条件5：收盘接近高点
            && afternoonHigherCenter              // 条件6：下午中枢高于上午
            && !tailFallBack                      // 条件7：尾盘无回落
            && !structureWeak
            && !tailTrap
            && !morningHighFailed;               // 硬规则：上午冲高失败禁止

        // 9. 健康洗盘：必须有明显回踩+快速修复，禁止用于阶梯推升
        bool hasWashout = aboveRatio < 0.65m && aboveRatio >= 0.45m  // 有时间在黄线下方
                          && risingStructure                           // 但低点仍在抬高
                          && tailClosePos > 0.6m;                     // 最终收回高位
        bool isHealthyWashout = hasWashout && !isStepwiseUp && !tailTrap;

        // 10. 危险覆盖：只有真正诱多才触发，结构偏弱不等于危险
        bool isDangerZone = tailTrap || (structureWeak && aboveRatio < 0.35m);

        // 11. 评分
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

        // 票型适配：趋势机构型/中军容量型用宽松标准，不要求暴力拉升
        // 趋势票正常表现：台阶震荡、回踩均线、尾盘整理，不应被判为弱
        if (!isDangerZone && style is StockStyle.TrendInstitutional or StockStyle.LargeCapVolume)
        {
            // 核心条件：长时间运行均线上方 + 尾盘未跳水 + 重心抬高 → 趋势偏强，55~70
            if (aboveRatio >= 0.60m && tailNoJump && risingStructure && !tailFallBack)
                score = Math.Max(score, 62);
            else if (aboveRatio >= 0.55m && tailNoJump && risingStructure)
                score = Math.Max(score, 55);
            // 结构中性但尾盘稳住 → 至少45，不能判弱
            else if (structureNeutral && tailNoJump && !tailFallBack)
                score = Math.Max(score, 48);
            else if (structureNeutral && tailNoJump)
                score = Math.Max(score, 42);
        }

        // 12. 形态（优先级：TailTrap > StepwiseUp > MainUpTrend > HealthyWashout > WeakRecovery）
        // 修复震荡：上午冲高失败后下午修复
        bool isRepairPattern = morningHighFailed || (morningRatio > afternoonRatio + 0.1m && !structureStrong);

        // 趋势震荡偏强：结构中性但重心抬高，不是诱多
        bool isTrendShock = structureNeutral && risingStructure && !tailTrap
                         && tailClosePos > 0.5m && !morningHighFailed;

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

        // 13. 等级
        AttackGrade grade = (tailTrap || isDangerZone) ? AttackGrade.C
            : morningHighFailed ? AttackGrade.B
            : pattern == IntradayPattern.MainUpTrend && tailClosePos > 0.8m ? AttackGrade.S
            : pattern == IntradayPattern.MainUpTrend || pattern == IntradayPattern.StepwiseUp ? AttackGrade.A
            : pattern == IntradayPattern.HealthyWashout || pattern == IntradayPattern.TrendShock ? AttackGrade.B
            : structureNeutral ? AttackGrade.B   // 中性结构最低给B，不能是C
            : AttackGrade.C;

        AttackWill will = score >= 65 ? AttackWill.Strong : score >= 40 ? AttackWill.Medium : AttackWill.Weak;

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

        Console.WriteLine("==================================================");
        Console.WriteLine("【基础分时数据】");
        Console.WriteLine("==================================================");
        Console.WriteLine($"1. minuteBars.Count = {n}");
        Console.WriteLine($"2. firstMinuteTime = {bars[0].Time}");
        Console.WriteLine($"3. lastMinuteTime = {bars[n - 1].Time}");
        Console.WriteLine($"4. usingRealMinuteData = true");
        Console.WriteLine("==================================================");
        Console.WriteLine("【黄线结构】");
        Console.WriteLine("==================================================");
        Console.WriteLine($"5. aboveAvgRatio = {aboveRatio:F4}");
        Console.WriteLine($"6. morningAboveAvgRatio = {morningRatio:F4}");
        Console.WriteLine($"7. afternoonAboveAvgRatio = {afternoonRatio:F4}");
        Console.WriteLine($"8. avgSlope (avgRising) = {avgRising}  vwap[0]={vwap[0]:F3} vwap[n-1]={vwap[n - 1]:F3}");
        Console.WriteLine($"9. afternoonSlope = {afternoonSlope:F4}  (下午vwap末-下午vwap首)/下午vwap首");
        Console.WriteLine("==================================================");
        Console.WriteLine("【高低点结构】");
        Console.WriteLine("==================================================");
        Console.WriteLine($"10. lowPointRisingScore = {lowPointRisingScore}");
        Console.WriteLine($"11. highPointRisingScore = {highPointRisingScore}");
        Console.WriteLine($"12. afternoonHighBreakCount = {afternoonHighBreakCount}");
        Console.WriteLine($"13. afternoonLowHigherThanMorning = {afternoonLowHigherThanMorning}");
        Console.WriteLine("==================================================");
        Console.WriteLine("【尾盘结构】");
        Console.WriteLine("==================================================");
        Console.WriteLine($"14. closePosition = {tailClosePos:F4}");
        Console.WriteLine($"15. tailPullUpScore = {tailRally}");
        Console.WriteLine($"16. tailFallBackScore = {tailFallBack}");
        Console.WriteLine($"17. tailHighHoldScore = {highShock}");
        Console.WriteLine("==================================================");
        Console.WriteLine("【形态评分】");
        Console.WriteLine("==================================================");
        Console.WriteLine($"18. stepUpScore = {stepCount}  isStepwiseUp={isStepwiseUp}");
        Console.WriteLine($"19. tailTrapScore = {tailTrap}");
        Console.WriteLine($"20. washScore = {isHealthyWashout}");
        Console.WriteLine($"21. repairScore = {repairScore}");
        Console.WriteLine("==================================================");
        Console.WriteLine("【最终分类】");
        Console.WriteLine("==================================================");
        Console.WriteLine($"22. structureType = {pattern}");
        Console.WriteLine($"23. attackGrade = {grade}");
        Console.WriteLine($"24. finalReason = {desc}");
        Console.WriteLine("==================================================");

        return new(score, will, pattern, desc, grade, isDangerZone);
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

        // 低开高走+收盘高位 = 阶梯推升（优先于健康洗盘）
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
