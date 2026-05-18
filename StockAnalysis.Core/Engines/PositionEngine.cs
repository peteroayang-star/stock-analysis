using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public enum PositionLevel { Low, Mid, High }
public enum AccelerationState { Normal, Accelerating, ExplosiveVolume, Fatigued }
public enum DetailedTrendStage
{
    Launch,          // 启动期
    TrendBuilding,   // 趋势增强
    MainUpEarly,     // 主升初期
    MainUpMid,       // 主升中期
    MainUpLate,      // 主升末端
    HighShock,       // 高位震荡
    DistributionRisk,// 出货风险
    TrendDecline     // 趋势衰退
}

public record PositionResult(
    PositionLevel Level,
    DetailedTrendStage Stage,
    AccelerationState Acceleration,
    decimal DistFromMA5,
    decimal DistFromMA10,
    decimal DistFromMA20,
    bool IsOverextended,
    bool IsVolumeExplosive,
    bool IsConsecutiveYang,
    string Description
);

public class PositionEngine
{
    private readonly IndicatorCalculator _calc = new();

    public PositionResult Evaluate(List<StockBar> bars, int index, VolumeResult vol,
        CycleResult cycle, SmartMoneyResult smartMoney,
        IndicatorCalculator.Indicators? ind = null)
    {
        ind ??= _calc.Calculate(bars, index);
        if (ind == null || index < 19) return new PositionResult(
            PositionLevel.Mid, DetailedTrendStage.TrendBuilding,
            AccelerationState.Normal, 0, 0, 0, false, false, false, "数据不足");

        var bar = bars[index];

        // ── 1. 位置判断 (距MA20偏离率) ──────────────
        decimal distMA5  = ind.MA5  > 0 ? (bar.Close - ind.MA5)  / ind.MA5  * 100 : 0;
        decimal distMA10 = ind.MA10 > 0 ? (bar.Close - ind.MA10) / ind.MA10 * 100 : 0;
        decimal distMA20 = ind.MA20 > 0 ? (bar.Close - ind.MA20) / ind.MA20 * 100 : 0;

        PositionLevel level = distMA20 switch
        {
            < -5m  => PositionLevel.Low,
            > 25m  => PositionLevel.High,
            > 15m  => PositionLevel.High,
            _      => PositionLevel.Mid
        };

        // 20日涨幅过大 → 高位
        var low20 = bars.Skip(index - 19).Min(b => b.Low);
        var gain20 = low20 > 0 ? (bar.Close - low20) / low20 * 100 : 0;
        if (gain20 > 40m) level = PositionLevel.High;
        else if (gain20 < -15m) level = PositionLevel.Low;

        // ── 2. 阶段判断 ──────────────────────────
        bool maBull = ind.MA5 > ind.MA10 && ind.MA10 > ind.MA20;
        bool maBear = ind.MA5 < ind.MA10 && ind.MA10 < ind.MA20;
        bool aboveMA20 = bar.Close > ind.MA20;
        bool macdUp = ind.DIF > ind.DEA && ind.MACD > 0;

        // 20日涨幅
        var gain20Pct = bars[index - 19].Close > 0
            ? (bar.Close - bars[index - 19].Close) / bars[index - 19].Close * 100 : 0;

        // 5日涨幅
        var gain5Pct = bars[index - 4].Close > 0
            ? (bar.Close - bars[index - 4].Close) / bars[index - 4].Close * 100 : 0;

        // 涨停次数
        int limitUps = 0;
        for (int i = Math.Max(1, index - 13); i <= index; i++)
            if (bars[i - 1].Close > 0 &&
                (bars[i].High - bars[i - 1].Close) / bars[i - 1].Close >= 0.095m)
                limitUps++;

        DetailedTrendStage stage;
        if (maBear && bar.Close < ind.MA20)
        {
            stage = gain20Pct < -10m ? DetailedTrendStage.TrendDecline
                  : DetailedTrendStage.HighShock;
        }
        else if (level == PositionLevel.High && !maBull)
        {
            stage = vol.State is VolumeState.VolumeDistribute or VolumeState.VolumeStall
                ? DetailedTrendStage.DistributionRisk
                : DetailedTrendStage.HighShock;
        }
        else if (level == PositionLevel.High && maBull)
        {
            stage = gain5Pct > 15m || limitUps >= 2
                ? DetailedTrendStage.MainUpLate
                : DetailedTrendStage.MainUpMid;
        }
        else if (maBull && gain20Pct > 15m && macdUp)
        {
            // 需要主升中期条件
            var forceReady = smartMoney.Behavior == SmartMoneyBehavior.AggressiveAttack ||
                             smartMoney.Behavior == SmartMoneyBehavior.Accumulation;
            var volContinuous = vol.State == VolumeState.AggressiveBuy;

            stage = forceReady && volContinuous && limitUps >= 1
                ? DetailedTrendStage.MainUpMid
                : DetailedTrendStage.MainUpEarly;
        }
        else if (maBull && aboveMA20 && macdUp)
        {
            stage = DetailedTrendStage.MainUpEarly;
        }
        else if (aboveMA20 && ind.MA5 > ind.MA10)
        {
            stage = DetailedTrendStage.TrendBuilding;
        }
        else if (bar.Close > ind.MA20 && !maBear)
        {
            stage = DetailedTrendStage.Launch;
        }
        else
        {
            stage = DetailedTrendStage.HighShock;
        }

        // ── 3. 加速度判断 ──────────────────────
        AccelerationState accel;
        var volRatio5 = bars.Skip(index - 4).Average(b => (double)b.Volume);
        var volRatio20 = bars.Skip(index - 19).Take(15).Average(b => (double)b.Volume);
        decimal volExplosion = (decimal)(volRatio20 > 0 ? volRatio5 / volRatio20 : 1);

        bool highTurnover = bar.Amount > 0 && vol.State == VolumeState.AggressiveBuy;

        if (gain5Pct > 25m && volExplosion > 2.5m)
            accel = AccelerationState.ExplosiveVolume;
        else if (gain5Pct > 12m && volExplosion > 1.5m)
            accel = AccelerationState.Accelerating;
        else if (gain5Pct > 5m && vol.State == VolumeState.ShrinkConsolidate)
            accel = AccelerationState.Fatigued;
        else
            accel = AccelerationState.Normal;

        // ── 4. 危险标志 ──────────────────────
        bool isOverextended = distMA5 > 12m || gain5Pct > 25m || volExplosion > 3m;
        bool isVolumeExplosive = volExplosion > 2.5m && highTurnover;
        bool isConsecutiveYang = true;
        for (int i = 0; i < 4 && (index - i) >= 0; i++)
            if (bars[index - i].Close <= bars[index - i].Open)
            { isConsecutiveYang = false; break; }

        // ── 5. 中文描述 ──────────────────────
        string desc = stage switch
        {
            DetailedTrendStage.Launch           => isOverextended ? "低位启动，不宜追涨" : "低位启动，可观察",
            DetailedTrendStage.TrendBuilding    => "趋势增强，量价待确认",
            DetailedTrendStage.MainUpEarly      => "主升初期，资金介入",
            DetailedTrendStage.MainUpMid        => isOverextended ? "主升中期但偏离过大" : "主升中期，趋势健康",
            DetailedTrendStage.MainUpLate       => "主升末端，追高风险极大",
            DetailedTrendStage.HighShock        => "高位震荡，方向不明",
            DetailedTrendStage.DistributionRisk => "出货风险，谨慎回避",
            DetailedTrendStage.TrendDecline     => "趋势衰退，不宜参与",
            _ => ""
        };

        return new PositionResult(
            level, stage, accel, Math.Round(distMA5, 1), Math.Round(distMA10, 1),
            Math.Round(distMA20, 1), isOverextended, isVolumeExplosive, isConsecutiveYang, desc);
    }

    /// <summary>生成页面统一展示结论</summary>
    public static string GetUnifiedRiskLabel(DetailedTrendStage stage, int riskScore, bool isOverextended)
    {
        string riskLabel = riskScore switch
        {
            <= 25 => "低",
            <= 40 => "中低",
            <= 55 => "中",
            <= 70 => "中高",
            _     => "高"
        };

        string stageLabel = stage switch
        {
            DetailedTrendStage.Launch           => "启动",
            DetailedTrendStage.TrendBuilding    => "增强",
            DetailedTrendStage.MainUpEarly      => "初升",
            DetailedTrendStage.MainUpMid        => "主升",
            DetailedTrendStage.MainUpLate       => "末升",
            DetailedTrendStage.HighShock        => "震荡",
            DetailedTrendStage.DistributionRisk => "出货",
            DetailedTrendStage.TrendDecline     => "衰退",
            _ => ""
        };

        return isOverextended
            ? $"{riskLabel}风险·{stageLabel}(偏离)"
            : $"{riskLabel}风险·{stageLabel}";
    }
}
