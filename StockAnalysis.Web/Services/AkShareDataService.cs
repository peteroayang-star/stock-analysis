using System.Text.Json;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

public record RealtimeQuote(decimal Price, decimal ChangePct, decimal Open, decimal High, decimal Low, long Volume, decimal Amount);

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

        // 6位数字直接当代码，尝试从字典或AKShare查名称
        if (input.Length == 6 && input.All(char.IsDigit))
        {
            var dictName = _dict.GetName(input);
            if (dictName != null) return (input, dictName);
            try
            {
                var resp = await _http.GetStringAsync($"http://127.0.0.1:5100/name/{input}");
                var json = JsonDocument.Parse(resp).RootElement;
                return (input, json.GetProperty("name").GetString() ?? input);
            }
            catch { return (input, input); }
        }

        // 调 AKShare /search 接口
        try
        {
            var resp = await _http.GetStringAsync($"http://127.0.0.1:5100/search/{Uri.EscapeDataString(input)}");
            var json = JsonDocument.Parse(resp).RootElement;
            return (json.GetProperty("code").GetString(), json.GetProperty("name").GetString());
        }
        catch { return (null, null); }
    }

    public async Task<RealtimeQuote?> TryGetRealtimeAsync(string code)
    {
        try
        {
            var json = await _http.GetStringAsync($"http://127.0.0.1:5100/realtime/{code}");
            var doc = JsonDocument.Parse(json).RootElement;
            return new RealtimeQuote(
                doc.GetProperty("price").GetDecimal(),
                doc.GetProperty("change_pct").GetDecimal(),
                doc.GetProperty("open").GetDecimal(),
                doc.GetProperty("high").GetDecimal(),
                doc.GetProperty("low").GetDecimal(),
                (long)doc.GetProperty("volume").GetDouble(),
                doc.GetProperty("amount").GetDecimal()
            );
        }
        catch { return null; }
    }

    /// <summary>
    /// 尝试获取股票行情
    /// </summary>
    /// <param name="code"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public async Task<List<StockBar>?> TryGetBarsAsync(string code, string name)
    {
        var cacheFile = Path.Combine(_cachePath, $"{code}.csv");

        if (File.Exists(cacheFile) && File.GetLastWriteTime(cacheFile).Date == DateTime.Today)
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

    public async Task<List<MinuteBar>?> TryGetMinuteBarsAsync(string code, DateTime? expectedDate = null)
    {
        try
        {
            var csv = await _http.GetStringAsync($"http://127.0.0.1:5100/minute/{code}");
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var all = new List<MinuteBar>();
            foreach (var line in lines.Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 6) continue;
                if (!DateTime.TryParse(p[0].Trim(), out var t)) continue;
                all.Add(new MinuteBar {
                    Time   = t,
                    Open   = decimal.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                    High   = decimal.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture),
                    Low    = decimal.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture),
                    Close  = decimal.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture),
                    Volume = long.Parse(p[5].Trim())
                });
            }
            if (all.Count == 0) return null;
            // 取最新交易日的分时数据（忽略 expectedDate，因为非交易日时接口返回上一交易日数据）
            var latestDate = all.Max(b => b.Time.Date);
            var result = all.Where(b => b.Time.Date == latestDate).ToList();
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    public async Task<List<string>> GetSectorsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("http://127.0.0.1:5100/sectors");
            var doc = JsonDocument.Parse(json).RootElement;
            return doc.GetProperty("sectors").EnumerateArray().Select(x => x.GetString()!).ToList();
        }
        catch { return []; }
    }

    public async Task<List<(string Code, string Name)>> GetSectorStocksAsync(string sector)
    {
        try
        {
            var json = await _http.GetStringAsync($"http://127.0.0.1:5100/sector/{Uri.EscapeDataString(sector)}");
            var doc = JsonDocument.Parse(json).RootElement;
            return doc.GetProperty("stocks").EnumerateArray()
                .Select(x => (x.GetProperty("code").GetString()!, x.GetProperty("name").GetString()!))
                .ToList();
        }
        catch { return []; }
    }
}
