using System.Globalization;
using System.Text;

namespace StockAnalysis.Web.Services;

public record RealTimeQuote(decimal Price, decimal Open, decimal PreClose, decimal ChangePct, long Volume, decimal High, decimal Low, decimal Amount, decimal Turnover);

public class TencentRealTimeService
{
    private readonly HttpClient _http;

    public TencentRealTimeService(HttpClient http) => _http = http;

    public async Task<RealTimeQuote?> GetAsync(string code)
    {
        try
        {
            var prefix = code.StartsWith("6") ? "sh" : "sz";
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = await _http.GetByteArrayAsync($"https://qt.gtimg.cn/q={prefix}{code}");
            var raw = Encoding.GetEncoding("GBK").GetString(bytes);
            // 格式: v_sz000539="1~名称~代码~当前价~昨收~今开~..."
            var start = raw.IndexOf('"') + 1;
            var end = raw.LastIndexOf('"');
            if (start < 0 || end <= start) return null;
            var fields = raw[start..end].Split('~');
            if (fields.Length < 35) return null;

            var price    = decimal.Parse(fields[3], CultureInfo.InvariantCulture);
            var preClose = decimal.Parse(fields[4], CultureInfo.InvariantCulture);
            var open     = decimal.Parse(fields[5], CultureInfo.InvariantCulture);
            var volume   = long.Parse(fields[6], CultureInfo.InvariantCulture);
            var high     = decimal.Parse(fields[33], CultureInfo.InvariantCulture);
            var low      = decimal.Parse(fields[34], CultureInfo.InvariantCulture);
            var amount    = fields.Length > 37 && decimal.TryParse(fields[37], NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0m;
            var turnover  = fields.Length > 38 && decimal.TryParse(fields[38], NumberStyles.Any, CultureInfo.InvariantCulture, out var t) ? t : 0m;
            var changePct = preClose == 0 ? 0 : Math.Round((price - preClose) / preClose * 100, 2);
            return new RealTimeQuote(price, open, preClose, changePct, volume, high, low, amount, turnover);
        }
        catch (Exception ex) { Console.WriteLine($"[TENCENT ERROR] {ex.Message}"); return null; }
    }
}
