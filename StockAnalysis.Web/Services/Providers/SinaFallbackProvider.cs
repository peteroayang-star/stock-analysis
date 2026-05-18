using System.Globalization;
using System.Text;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services.Providers;

/// <summary>新浪财经实时行情兜底数据源</summary>
public class SinaFallbackProvider : IMarketDataProvider
{
    private readonly HttpClient _http;

    public string Name => "Sina";
    public int Priority => 4;

    public SinaFallbackProvider(HttpClient http) => _http = http;

    public Task<DataSourceResult<List<StockBar>>> GetBarsAsync(string code)
        => Task.FromResult(DataSourceResult<List<StockBar>>.Fail("新浪不提供历史K线", Name));

    public async Task<DataSourceResult<RealTimeQuote>> GetRealtimeAsync(string code)
    {
        try
        {
            var prefix = code.StartsWith("6") ? "sh" : "sz";
            var url = $"https://hq.sinajs.cn/list={prefix}{code}";
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = await _http.GetByteArrayAsync(url);
            var raw = Encoding.GetEncoding("GBK").GetString(bytes);
            // 格式: var hq_str_sh600000="名称,今开,昨收,当前价,最高,最低,..."
            var start = raw.IndexOf('"') + 1;
            var end = raw.LastIndexOf('"');
            if (start <= 0 || end <= start)
                return DataSourceResult<RealTimeQuote>.Fail($"新浪行情解析失败 [{code}]", Name);

            var fields = raw[start..end].Split(',');
            if (fields.Length < 9)
                return DataSourceResult<RealTimeQuote>.Fail($"新浪行情字段不足 [{code}]", Name);

            var price = decimal.Parse(fields[3], CultureInfo.InvariantCulture);
            var preClose = decimal.Parse(fields[2], CultureInfo.InvariantCulture);
            var open = decimal.Parse(fields[1], CultureInfo.InvariantCulture);
            var high = decimal.Parse(fields[4], CultureInfo.InvariantCulture);
            var low = decimal.Parse(fields[5], CultureInfo.InvariantCulture);
            var volume = long.Parse(fields[8], CultureInfo.InvariantCulture);
            var amount = fields.Length > 9 && decimal.TryParse(fields[9], NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0m;
            var changePct = preClose == 0 ? 0 : Math.Round((price - preClose) / preClose * 100, 2);

            return DataSourceResult<RealTimeQuote>.Ok(
                new RealTimeQuote(price, open, preClose, changePct, volume, high, low, amount, 0), Name);
        }
        catch (Exception ex)
        {
            return DataSourceResult<RealTimeQuote>.Fail(
                $"新浪行情调用异常 [{code}]: {ex.Message}", Name);
        }
    }
}
