using System.Collections.Concurrent;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>数据源路由器：按优先级依次尝试各Provider，返回首个成功或汇总错误</summary>
public class ProviderRouter
{
    private readonly IMarketDataProvider[] _providers;

    public ConcurrentDictionary<string, int> FailCounts { get; } = new();
    public ConcurrentDictionary<string, string?> LastErrors { get; } = new();

    public ProviderRouter(IEnumerable<IMarketDataProvider> providers)
    {
        _providers = providers.OrderBy(p => p.Priority).ToArray();
    }

    public async Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code)
    {
        var errors = new List<string>();

        foreach (var provider in _providers)
        {
            var result = await provider.GetBarsAsync(code);
            if (result.Success && result.Data is { Count: > 0 })
                return result;

            if (!result.Success)
            {
                FailCounts.AddOrUpdate(provider.Name, 1, (_, c) => c + 1);
                LastErrors[provider.Name] = result.Error;
                errors.Add($"[{provider.Name}] {result.Error}");
            }
        }

        return DataSourceResult<List<StockBar>>.Fail(
            $"所有数据源均获取 [{code}] 失败: {string.Join("; ", errors)}", "Router");
    }

    public async Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code)
    {
        var errors = new List<string>();

        foreach (var provider in _providers)
        {
            var result = await provider.GetRealtimeAsync(code);
            if (result.Success && result.Data != null)
                return result;

            if (!result.Success)
            {
                FailCounts.AddOrUpdate(provider.Name, 1, (_, c) => c + 1);
                LastErrors[provider.Name] = result.Error;
                errors.Add($"[{provider.Name}] {result.Error}");
            }
        }

        return DataSourceResult<RealTimeQuote>.Fail(
            $"所有数据源均获取实时行情 [{code}] 失败: {string.Join("; ", errors)}", "Router");
    }
}
