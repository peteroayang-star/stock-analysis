using StockAnalysis.Core.Engines;

namespace StockAnalysis.Core.Models;

/// <summary>票型风格</summary>
public enum StockStyle
{
    TrendInstitutional, // 趋势机构型：台阶上涨、MA向上、缓慢放量
    EmotionSpeculative, // 妖股情绪型：高频涨停、高换手、情绪爆发
    LargeCapVolume,     // 中军容量型：大市值、震荡推进、机构承接
    BottomLaunch,       // 低位试盘型：底部放量异动、首次突破
    DistributionDecline // 出货衰退型：放量滞涨、跌破平台、连续阴跌
}


/// <summary>市场阶段（主力行为视角）</summary>
public enum MarketStage
{
    Accumulation,   // 底部吸筹期
    LaunchTest,     // 启动试盘期
    WashoutDip,     // 分歧洗筹期
    TrendRelay,     // 趋势中继期
    MainUpAccel,    // 主升加速期
    TopDistribute,  // 高位派发期
    Exhaustion      // 退潮衰竭期
}

public record MainForceBehaviorResult(
    int WashProbability,
    int DistributionProbability,
    int BreakoutProbability,
    int TrendHealthScore,
    int ControlStrength,
    int LockupScore,
    List<string> BehaviorTags,
    string Analysis,
    MarketStage Stage
);

/// <summary>分时形态</summary>
public enum IntradayPattern
{
    MainUpTrend,    // 主升趋势
    StepwiseUp,     // 阶梯式推升（控节奏推升）
    HealthyWashout, // 健康洗盘
    TrendShock,     // 趋势震荡偏强（短线分歧但趋势未坏）
    WeakRecovery,   // 弱势修复
    TailTrap,       // 尾盘诱多
    SmartExit       // 主力撤退
}

/// <summary>主力进攻意愿</summary>
public enum AttackWill { Strong, Medium, Weak }

/// <summary>资金进攻等级 S=主升抢筹 A=阶梯推升/强势承接 B=健康洗盘/修复震荡 C=尾盘诱多/主力撤退</summary>
public enum AttackGrade { S, A, B, C }

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

/// <summary>板块情绪周期</summary>
public enum SectorEmotionCycle { IcePoint, Recovery, Divergence, Consensus, Climax, Decline }

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
    /// <summary>支撑位（MA20）是否已跌破</summary>
    public bool SupportBroken { get; set; }
    /// <summary>周期阶段描述（启动/分歧/一致/主升/派发/结束）</summary>
    public string CycleStage { get; set; } = "";
    /// <summary>资金态度描述（放量进攻/缩量整理等）</summary>
    public string VolumeDescription { get; set; } = "";
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
    /// <summary>是否为情绪龙头股（涨停多/主动进攻/高位博弈）</summary>
    public bool IsEmotionLeader { get; set; }
    /// <summary>分时主动强度评分（0-100）</summary>
    public int IntradayStrengthScore { get; set; }
    /// <summary>主力进攻意愿（强/中/弱）</summary>
    public string AttackWillDescription { get; set; } = "";
    /// <summary>分时形态描述（主升趋势/健康洗盘/弱势修复/尾盘诱多/主力撤退）</summary>
    public string IntradayPattern { get; set; } = "";
    /// <summary>分时形态枚举，用于视图判断</summary>
    public IntradayPattern IntradayPatternType { get; set; }
    /// <summary>次日涨停潜力评分（0-100，综合分时强度、5日线、换手率、量能）</summary>
    public int NextDayLimitUpScore { get; set; }
    /// <summary>资金进攻等级 S/A/B/C</summary>
    public AttackGrade AttackGrade { get; set; }
    /// <summary>危险覆盖：弱势结构触发，隐藏仓位/涨停潜力</summary>
    public bool IntradayDangerZone { get; set; }
    /// <summary>票型风格</summary>
    public StockStyle StockStyle { get; set; }
    /// <summary>主力行为识别结果</summary>
    public MainForceBehaviorResult? MainForceBehavior { get; set; }
    /// <summary>主升浪平台识别结果</summary>
    public MainUpPlatformResult? MainUpPlatform { get; set; }
    /// <summary>龙虎榜行为分析结果</summary>
    public DragonTigerBehaviorResult? DragonTiger { get; set; }
    /// <summary>板块情绪周期结果</summary>
    public SectorEmotionResult? SectorEmotion { get; set; }
    /// <summary>筹码控盘承接结果</summary>
    public ChipControlResult? ChipControl { get; set; }
    /// <summary>板块共振分析结果</summary>
    public SectorResonanceResult? SectorResonance { get; set; }
}
