namespace StockAnalysis.Web.Models;

/// <summary>AI分析用的结构化摘要，禁止包含原始K线数组/分钟线/新闻全文</summary>
public class StructuredAnalysisResult
{
    public string StockCode { get; set; } = "";
    public string StockName { get; set; } = "";
    public string TrendState { get; set; } = "";       // "上涨偏强" / "震荡中性" / "下跌偏弱"
    public string VolumeState { get; set; } = "";      // "放量进攻" / "缩量整理" 等
    public string IntradayState { get; set; } = "";    // "主升趋势" / "健康洗盘" 等
    public string RiskLevel { get; set; } = "";        // "低风险" / "中风险" / "高风险" / "极高风险"
    public int RiskScore { get; set; }
    public int TrendRisk { get; set; }
    public int VolatilityRisk { get; set; }
    public int SentimentRisk { get; set; }
    public string Decision { get; set; } = "";         // "可以买入" / "观察等待" / "持有不动" 等
    public decimal SupportPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal WatchPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public string CycleStage { get; set; } = "";
    public string? SectorName { get; set; }
    public string? SectorEmotion { get; set; }
    public string ActionAdvice { get; set; } = "";
    public bool IsEmotionLeader { get; set; }
    public int LimitUpCountIn14Days { get; set; }
    public string? SmartMoneyDescription { get; set; }
    public string? MainUpPlatformSummary { get; set; }
    public int IntradayStrengthScore { get; set; }
    public List<string> Reasons { get; set; } = [];
}
