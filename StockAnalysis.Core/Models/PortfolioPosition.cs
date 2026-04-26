namespace StockAnalysis.Core.Models;

/// <summary>持仓记录，用于持仓模式分析</summary>
public class PortfolioPosition
{
    /// <summary>股票代码</summary>
    public string Code { get; set; } = "";
    /// <summary>股票名称</summary>
    public string Name { get; set; } = "";
    /// <summary>买入成本价（元）</summary>
    public decimal CostPrice { get; set; }
    /// <summary>持股数量（股）</summary>
    public int Shares { get; set; }
    /// <summary>买入日期</summary>
    public DateTime BuyDate { get; set; }
}
