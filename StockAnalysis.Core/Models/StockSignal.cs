namespace StockAnalysis.Core.Models;

/// <summary>买入信号类型</summary>
public enum BuySignalType
{
    /// <summary>无信号</summary>
    None,
    /// <summary>倍量突破：收盘突破近N日高点且成交量放大</summary>
    VolumeBreakout,
    /// <summary>缩量回踩：多头排列下缩量回踩MA10</summary>
    PullbackSupport,
    /// <summary>凹量洗盘：连续N日缩量后收盘守住MA20</summary>
    VolumeWashout,
    /// <summary>趋势回调：上涨趋势中缩量回调至关键均线后转强</summary>
    TrendPullback
}

/// <summary>趋势方向</summary>
public enum Trend { Up, Down, Sideways }

/// <summary>交易决策</summary>
public enum Decision
{
    /// <summary>可以买入</summary>
    Buy,
    /// <summary>轻仓试错</summary>
    TryBuy,
    /// <summary>观察等待</summary>
    Watch,
    /// <summary>持有不动</summary>
    Hold,
    /// <summary>建议减仓</summary>
    Reduce,
    /// <summary>止损离场</summary>
    Sell,
    /// <summary>暂时观望，不参与</summary>
    Ignore
}

/// <summary>趋势阶段</summary>
public enum TrendStage { EarlyUp, MidUp, LateUp, Sideways, Down }

/// <summary>交易模式</summary>
public enum TradingMode
{
    /// <summary>选股模式：判断是否买入</summary>
    Candidate,
    /// <summary>持仓模式：判断持有/减仓/止损</summary>
    Portfolio
}

/// <summary>主力行为</summary>
public enum SmartMoneyBehavior
{
    Accumulation,     // 吸筹
    Washout,          // 洗盘
    DivergenceSwap,   // 分歧换手
    AggressiveAttack, // 主动进攻
    HighShock,        // 高位震荡
    Distribution,     // 派发
    Dumping,          // 出货
    None              // 无主力行为
}

/// <summary>单只股票的分析结果与交易决策</summary>
public class StockSignal
{
    /// <summary>股票代码</summary>
    public string Code { get; set; } = "";
    /// <summary>股票名称</summary>
    public string Name { get; set; } = "";
    /// <summary>分析日期</summary>
    public DateTime Date { get; set; }
    /// <summary>当日收盘价</summary>
    public decimal Close { get; set; }
    /// <summary>检测到的买入信号类型</summary>
    public BuySignalType SignalType { get; set; }
    /// <summary>风险评分（0-100，越高风险越大）</summary>
    public int RiskScore { get; set; }
    /// <summary>最终交易决策</summary>
    public Decision Decision { get; set; }
    /// <summary>中文风险原因列表</summary>
    public List<string> Reasons { get; set; } = [];
    /// <summary>支撑位（MA20），跌破需警惕</summary>
    public decimal? SupportPrice { get; set; }
    /// <summary>止损位（MA20 × 0.98），跌破应离场</summary>
    public decimal? StopLossPrice { get; set; }
    /// <summary>观察位（MA10 × 1.02），突破可加仓</summary>
    public decimal? WatchPrice { get; set; }
    /// <summary>目标价（MA10 × 1.08），止盈参考</summary>
    public decimal? TargetPrice { get; set; }
    /// <summary>趋势阶段</summary>
    public TrendStage TrendStage { get; set; }
    /// <summary>趋势方向</summary>
    public Trend Trend { get; set; }
    /// <summary>操作建议（当前该干嘛）</summary>
    public string ActionAdvice { get; set; } = "";
    /// <summary>建议仓位比例（0-100）</summary>
    public int PositionPct { get; set; }
    /// <summary>14天内触及涨停次数</summary>
    public int LimitUpCountIn14Days { get; set; }
    /// <summary>近10日平均成交量（手）</summary>
    public long AvgVolume10 { get; set; }
    /// <summary>支撑位（MA20）是否已跌破</summary>
    public bool SupportBroken { get; set; }
    /// <summary>价格结构异常（深度跌破支撑）</summary>
    public bool StructureAbnormal { get; set; }
    /// <summary>信号强度：强/中/弱</summary>
    public string SignalStrength { get; set; } = "";
    /// <summary>周期阶段描述（启动/分歧/一致/主升/派发/结束）</summary>
    public string CycleStage { get; set; } = "";
    /// <summary>资金态度描述（放量进攻/缩量整理等）</summary>
    public string VolumeDescription { get; set; } = "";
    /// <summary>交易价值评分（0-100，越高越值得参与，独立于风险分）</summary>
    public int TradeValueScore { get; set; }
    /// <summary>主力行为</summary>
    public SmartMoneyBehavior SmartMoney { get; set; }
    /// <summary>主力行为描述</summary>
    public string SmartMoneyDescription { get; set; } = "";
    /// <summary>趋势风险（0-100）</summary>
    public int TrendRisk { get; set; }
    /// <summary>波动风险（0-100）</summary>
    public int VolatilityRisk { get; set; }
    /// <summary>情绪风险（0-100）</summary>
    public int SentimentRisk { get; set; }
}
