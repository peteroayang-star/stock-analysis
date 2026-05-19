namespace StockAnalysis.Core.Models;

/// <summary>交易视角下的公司逻辑摘要（非F10资料页）</summary>
public class TradingLogicResult
{
    /// <summary>主营业务（一句话）</summary>
    public string MainBusiness { get; set; } = "";
    /// <summary>核心产品</summary>
    public string CoreProduct { get; set; } = "";
    /// <summary>核心概念</summary>
    public string CoreConcept { get; set; } = "";
    /// <summary>所属产业链位置</summary>
    public string IndustryChain { get; set; } = "";
    /// <summary>市场属性：大盘/中盘/小盘，成长/价值/周期</summary>
    public string MarketAttribute { get; set; } = "";
    /// <summary>资金风格：机构趋势/游资情绪/量化/混合</summary>
    public string CapitalStyle { get; set; } = "";
    /// <summary>财报摘要（≤3句话）</summary>
    public string FinancialSummary { get; set; } = "";
    /// <summary>当前炒作逻辑分类</summary>
    public string HypeLogic { get; set; } = "";
    /// <summary>持续性评估</summary>
    public string Sustainability { get; set; } = "";
    /// <summary>风险点列表</summary>
    public List<string> RiskPoints { get; set; } = [];
    /// <summary>交易逻辑摘要 (100-200字)</summary>
    public string TradingSummary { get; set; } = "";
}
