using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services.Providers;

/// <summary>腾讯实时行情数据源</summary>
public class TencentRealtimeProvider : IMarketDataProvider
{
    private readonly TencentRealTimeService _tencent;

    public string Name => "Tencent";
    public int Priority => 3;

    public TencentRealtimeProvider(TencentRealTimeService tencent) => _tencent = tencent;

    public Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code)
        => Task.FromResult(DataSourceResult<List<StockBar>>.Fail("腾讯不提供历史K线", Name));

    public async Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code)
    {
        try
        {
            var quote = await _tencent.GetAsync(code);
            if (quote != null)
                return DataSourceResult<RealTimeQuote>.Ok(quote, Name);

            return DataSourceResult<RealTimeQuote>.Fail(
                $"腾讯行情返回空数据 [{code}]", Name);
        }
        catch (Exception ex)
        {
            return DataSourceResult<RealTimeQuote>.Fail(
                $"腾讯行情调用异常 [{code}]: {ex.Message}", Name);
        }
    }
}
