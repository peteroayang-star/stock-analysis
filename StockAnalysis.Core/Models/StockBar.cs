namespace StockAnalysis.Core.Models;

/// <summary>单根 K 线数据（日线）</summary>
public class StockBar
{
    /// <summary>股票代码，如 000001</summary>
    public string Code { get; set; } = "";
    /// <summary>股票名称，如 平安银行</summary>
    public string Name { get; set; } = "";
    /// <summary>交易日期</summary>
    public DateTime Date { get; set; }
    /// <summary>开盘价（元）</summary>
    public decimal Open { get; set; }
    /// <summary>最高价（元）</summary>
    public decimal High { get; set; }
    /// <summary>最低价（元）</summary>
    public decimal Low { get; set; }
    /// <summary>收盘价（元）</summary>
    public decimal Close { get; set; }
    /// <summary>成交量（手）</summary>
    public long Volume { get; set; }
    /// <summary>成交额（元）</summary>
    public decimal Amount { get; set; }
}
