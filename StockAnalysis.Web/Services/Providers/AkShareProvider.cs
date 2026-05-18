using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services.Providers;

/// <summary>AKShare数据源适配器：封装 AkShareDataService，将 null 转为明确错误</summary>
public class AkShareProvider : IMarketDataProvider
{
    private readonly AkShareDataService _akShare;

    public string Name => "AKShare";
    public int Priority => 3; // TencentKline 优先级更高

    public AkShareProvider(AkShareDataService akShare) => _akShare = akShare;

    public async Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code)
    {
        try
        {
            var bars = await _akShare.TryGetBarsAsync(code, code);
            if (bars != null && bars.Count > 0)
                return DataSourceResult<List<StockBar>>.Ok(bars, Name);

            return DataSourceResult<List<StockBar>>.Fail(
                $"AKShare 返回空数据 [{code}]", Name);
        }
        catch (Exception ex)
        {
            return DataSourceResult<List<StockBar>>.Fail(
                $"AKShare 调用异常 [{code}]: {ex.Message}", Name);
        }
    }

    public Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code)
        => Task.FromResult(DataSourceResult<RealTimeQuote>.Fail("AKShare 不提供实时行情", Name));
}
