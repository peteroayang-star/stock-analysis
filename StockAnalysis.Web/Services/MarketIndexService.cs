using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>
/// 市场指数服务 — 提供大盘 K 线数据作为市场环境参考。
/// 当前使用 159919（沪深300ETF）作为市场代理，相比单只银行股更能代表整体市场趋势。
/// 后续可升级为真实指数数据（需 Python 服务支持 index 端点）。
/// </summary>
public class MarketIndexService
{
    private readonly MarketDataService _marketData;

    /// <summary>市场代理代码：沪深300 ETF（深交所上市，跟踪沪深300指数）</summary>
    public const string MarketProxyCode = "159919";

    public MarketIndexService(MarketDataService marketData)
    {
        _marketData = marketData;
    }

    /// <summary>获取市场代理 K 线数据，失败返回 null</summary>
    public async Task<List<StockBar>?> GetMarketBarsAsync()
    {
        try
        {
            var (bars, _) = await _marketData.TryGetBarsAsync(MarketProxyCode);
            return bars;
        }
        catch
        {
            return null;
        }
    }
}
