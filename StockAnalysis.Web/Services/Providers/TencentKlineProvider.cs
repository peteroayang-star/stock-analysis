using System.Globalization;
using System.Text.Json;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services.Providers;

/// <summary>腾讯K线直连数据源：直接调用腾讯免费API获取前复权日K线，不依赖Python/Flask/AKShare</summary>
public class TencentKlineProvider : IMarketDataProvider
{
    private readonly HttpClient _http;

    public string Name => "TencentKline";
    public int Priority => 2; // 介于 LocalCSV(1) 和 AKShare(3) 之间

    public TencentKlineProvider(HttpClient http) => _http = http;

    public async Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code)
    {
        try
        {
            var prefix = code.StartsWith("6") ? "sh" : "sz";
            var symbol = $"{prefix}{code}";
            var url = $"http://web.ifzq.gtimg.cn/appstock/app/fqkline/get" +
                      $"?_var=kline_dayqfq&param={symbol},day,,,640,qfq";

            var raw = await _http.GetStringAsync(url);

            // 格式: kline_dayqfq={...}
            var eqIdx = raw.IndexOf('=');
            if (eqIdx < 0)
                return DataSourceResult<List<StockBar>>.Fail($"腾讯K线响应格式异常 [{code}]", Name);

            var json = raw[(eqIdx + 1)..];
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data").GetProperty(symbol);
            var dayData = data.TryGetProperty("qfqday", out var qfq) ? qfq : data.GetProperty("day");

            var bars = new List<StockBar>();
            foreach (var bar in dayData.EnumerateArray())
            {
                var fields = new string[6];
                int i = 0;
                foreach (var f in bar.EnumerateArray())
                {
                    if (i >= 6) break;
                    fields[i++] = f.ToString();
                }
                if (fields[0] == null || fields[1] == null) continue;

                if (!DateTime.TryParseExact(fields[0], new[] { "yyyy-MM-dd", "yyyyMMdd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                bars.Add(new StockBar
                {
                    Code = code,
                    Name = code,
                    Date = date,
                    Open = ParseDecimal(fields[1]),
                    Close = ParseDecimal(fields[2]),
                    High = ParseDecimal(fields[3]),
                    Low = ParseDecimal(fields[4]),
                    Volume = ParseLong(fields[5]),
                    Amount = 0
                });
            }

            if (bars.Count == 0)
                return DataSourceResult<List<StockBar>>.Fail($"腾讯K线无数据 [{code}]", Name);

            return DataSourceResult<List<StockBar>>.Ok(
                bars.OrderBy(b => b.Date).ToList(), Name);
        }
        catch (Exception ex)
        {
            return DataSourceResult<List<StockBar>>.Fail(
                $"腾讯K线调用异常 [{code}]: {ex.Message}", Name);
        }
    }

    public Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code)
        => Task.FromResult(DataSourceResult<RealTimeQuote>.Fail("TencentKline不提供实时行情", Name));

    private static decimal ParseDecimal(string s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long ParseLong(string s)
        => long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
