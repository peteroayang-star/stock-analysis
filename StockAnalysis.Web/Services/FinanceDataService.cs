using System.Text.Json;

namespace StockAnalysis.Web.Services;

public record FinanceData(
    double[] Revenue,
    double[] NetProfit,
    double[] NetMargin,
    double[] RevenueYoy,
    double[] ProfitYoy,
    string[] Periods
);

public class FinanceDataService
{
    private readonly HttpClient _http;

    public FinanceDataService(HttpClient http) => _http = http;

    public async Task<FinanceData?> GetAsync(string code)
    {
        try
        {
            var json = await _http.GetStringAsync($"http://127.0.0.1:5100/finance/{code}");
            var doc = JsonDocument.Parse(json).RootElement;
            return new FinanceData(
                Revenue:    doc.GetProperty("revenue").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                NetProfit:  doc.GetProperty("net_profit").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                NetMargin:  doc.GetProperty("net_margin").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                RevenueYoy: doc.GetProperty("revenue_yoy").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                ProfitYoy:  doc.GetProperty("profit_yoy").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                Periods:    doc.GetProperty("periods").EnumerateArray().Select(x => x.GetString()!).ToArray()
            );
        }
        catch { return null; }
    }
}
