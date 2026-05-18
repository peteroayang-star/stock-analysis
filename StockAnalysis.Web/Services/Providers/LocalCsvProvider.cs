using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services.Providers;

/// <summary>本地CSV数据源：优先从 wwwroot/data/ 和 Data/SystemStocks/ 读取</summary>
public class LocalCsvProvider : IMarketDataProvider
{
    private readonly DataImporter _importer;
    private readonly string _webDataPath;
    private readonly string _systemDataPath;

    public string Name => "LocalCSV";
    public int Priority => 1;

    public LocalCsvProvider(DataImporter importer, IWebHostEnvironment env)
    {
        _importer = importer;
        _webDataPath = Path.Combine(env.WebRootPath, "data");
        _systemDataPath = Path.Combine(env.ContentRootPath, "..", "Data", "SystemStocks");
    }

    public Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code)
    {
        // 优先 wwwroot/data/（内置数据），其次 Data/SystemStocks/（AKShare缓存）
        var paths = new[] {
            Path.Combine(_webDataPath, $"{code}.csv"),
            Path.Combine(_systemDataPath, $"{code}.csv")
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var bars = _importer.ImportCsv(path, code, code);
                    if (bars.Count > 0)
                        return Task.FromResult(DataSourceResult<List<StockBar>>.Ok(bars, Name));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(DataSourceResult<List<StockBar>>.Fail(
                        $"本地文件读取失败 [{code}]: {ex.Message}", Name));
                }
            }
        }

        return Task.FromResult(DataSourceResult<List<StockBar>>.Fail(
            $"本地文件不存在 [{code}]", Name));
    }

    public Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code)
        => Task.FromResult(DataSourceResult<RealTimeQuote>.Fail("LocalCSV不支持实时行情", Name));
}
