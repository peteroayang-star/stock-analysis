using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>统一行情数据提供者接口</summary>
public interface IMarketDataProvider
{
    /// <summary>提供者名称，用于日志和数据源追踪</summary>
    string Name { get; }

    /// <summary>优先级，越小越先尝试（1=LocalCSV, 2=AKShare, 3=Tencent, 4=Sina）</summary>
    int Priority { get; }

    /// <summary>获取历史K线</summary>
    Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code);

    /// <summary>获取实时行情</summary>
    Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code);
}
