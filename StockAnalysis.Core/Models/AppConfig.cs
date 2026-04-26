namespace StockAnalysis.Core.Models;

/// <summary>应用全局配置，从 appsettings.json 绑定</summary>
public class AppConfig
{
    /// <summary>风险评分阈值配置</summary>
    public RiskConfig Risk { get; set; } = new();
    /// <summary>买入信号检测参数</summary>
    public SignalConfig Signal { get; set; } = new();
    /// <summary>股票过滤条件</summary>
    public FilterConfig Filter { get; set; } = new();
}

/// <summary>风险评分决策阈值</summary>
public class RiskConfig
{
    /// <summary>风险分 ≤ 此值时决策为 Buy（默认 30）</summary>
    public int BuyMaxScore { get; set; } = 30;
    /// <summary>风险分 ≤ 此值时决策为 Watch（默认 50）</summary>
    public int WatchMaxScore { get; set; } = 50;
    /// <summary>风险分 > 此值时决策为 Sell（默认 65）</summary>
    public int SellScore { get; set; } = 65;
}

/// <summary>买入信号检测参数</summary>
public class SignalConfig
{
    /// <summary>突破信号回望天数（默认 20 日）</summary>
    public int BreakoutDays { get; set; } = 20;
    /// <summary>突破信号所需量能倍数（默认 1.8 倍均量）</summary>
    public decimal VolumeMultiplier { get; set; } = 1.8m;
    /// <summary>缩量回踩信号的量能收缩比（默认 0.7，即低于均量 70%）</summary>
    public decimal ShrinkVolumeRatio { get; set; } = 0.7m;
    /// <summary>回踩 MA10 的允许偏差比例（默认 ±2%）</summary>
    public decimal PullbackNearMARatio { get; set; } = 0.02m;
    /// <summary>凹量洗盘连续缩量天数（默认 3 日）</summary>
    public int WashoutDays { get; set; } = 3;
}

/// <summary>股票过滤条件，不满足则跳过分析</summary>
public class FilterConfig
{
    /// <summary>最低日均成交额（百万元），低于此值过滤（默认 5M）</summary>
    public double MinAmountMillionYuan { get; set; } = 5.0;
    /// <summary>最低上市天数，低于此值过滤（默认 60 日）</summary>
    public int MinListedDays { get; set; } = 60;
}
