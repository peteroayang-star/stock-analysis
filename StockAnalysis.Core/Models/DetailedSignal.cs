namespace StockAnalysis.Core.Models;

/// <summary>包含中文建议的详细信号，供复盘和报告使用</summary>
public class DetailedSignal
{
    /// <summary>股票代码</summary>
    public string Code { get; set; } = "";
    /// <summary>股票名称</summary>
    public string Name { get; set; } = "";
    /// <summary>分析日期</summary>
    public DateTime Date { get; set; }
    /// <summary>当日收盘价</summary>
    public decimal Close { get; set; }
    /// <summary>买入信号类型</summary>
    public BuySignalType Signal { get; set; }
    /// <summary>风险评分（0-100）</summary>
    public int RiskScore { get; set; }
    /// <summary>交易决策</summary>
    public Decision Decision { get; set; }
    /// <summary>中文风险原因列表</summary>
    public List<string> RiskReasons { get; set; } = [];
    /// <summary>中文操作建议，直接告诉交易员该怎么做</summary>
    public string Suggestion { get; set; } = "";
    /// <summary>止损价位（持仓模式专用）</summary>
    public decimal? StopLoss { get; set; }
    /// <summary>观察价位（持仓模式专用）</summary>
    public decimal? WatchLevel { get; set; }
}
