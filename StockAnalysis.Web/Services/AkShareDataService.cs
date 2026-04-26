using System.Text.Json;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>通过本地 AKShare HTTP 服务获取行情，并缓存到 Data/SystemStocks/</summary>
public class AkShareDataService
{
    private readonly DataImporter _importer;
    private readonly StockDictionaryService _dict;
    private readonly HttpClient _http;
    private readonly string _cachePath;

    public AkShareDataService(DataImporter importer, StockDictionaryService dict,
        HttpClient http, IWebHostEnvironment env)
    {
        _importer = importer;
        _dict = dict;
        _http = http;
        _cachePath = Path.Combine(env.ContentRootPath, "..", "Data", "SystemStocks");
        Directory.CreateDirectory(_cachePath);
    }

    /// <summary>将名称或代码解析为股票代码，优先本地字典，再查 AKShare</summary>
    public async Task<(string? Code, string? Name)> ResolveAsync(string input)
    {
        input = input.Trim();

        // 本地字典
        var code = _dict.GetCode(input);
        if (code != null) return (code, _dict.GetName(code) ?? input);

        // 6位数字直接当代码
        if (input.Length == 6 && input.All(char.IsDigit))
            return (input, input);

        // 调 AKShare /search 接口
        try
        {
            var resp = await _http.GetStringAsync($"http://127.0.0.1:5100/search/{Uri.EscapeDataString(input)}");
            var json = JsonDocument.Parse(resp).RootElement;
            return (json.GetProperty("code").GetString(), json.GetProperty("name").GetString());
        }
        catch { return (null, null); }
    }

    public async Task<List<StockBar>?> TryGetBarsAsync(string code, string name)
    {
        var cacheFile = Path.Combine(_cachePath, $"{code}.csv");

        if (File.Exists(cacheFile) && File.GetLastWriteTime(cacheFile) > DateTime.Now.AddDays(-1))
            return _importer.ImportCsv(cacheFile, code, name);

        try
        {
            var csv = await _http.GetStringAsync($"http://127.0.0.1:5100/stock/{code}");
            await File.WriteAllTextAsync(cacheFile, csv);
            return _importer.ImportCsv(cacheFile, code, name);
        }
        catch
        {
            return File.Exists(cacheFile) ? _importer.ImportCsv(cacheFile, code, name) : null;
        }
    }
}
