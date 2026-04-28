using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>统一数据入口：本地内置 → AKShare缓存/接口 → 失败返回null</summary>
public class MarketDataService
{
    private readonly DataImporter _importer;
    private readonly AkShareDataService _akShare;
    private readonly TencentRealTimeService _tencent;
    private readonly string _builtinPath;

    public MarketDataService(DataImporter importer, AkShareDataService akShare, TencentRealTimeService tencent, IWebHostEnvironment env)
    {
        _importer = importer;
        _akShare = akShare;
        _tencent = tencent;
        _builtinPath = Path.Combine(env.WebRootPath, "data");
    }

    public async Task<(List<StockBar>? Bars, string? Error)> TryGetBarsAsync(string input)
    {
        var (code, name) = await _akShare.ResolveAsync(input);
        if (code == null)
            return (null, "未能识别该股票，请输入6位代码或正确的股票名称");

        // 内置数据优先
        var builtinFile = Path.Combine(_builtinPath, $"{code}.csv");
        if (File.Exists(builtinFile))
            return (_importer.ImportCsv(builtinFile, code, name ?? code), null);

        // AKShare 历史数据
        var bars = await _akShare.TryGetBarsAsync(code, name ?? code);
        if (bars != null)
        {
            // 用腾讯实时接口补充今日数据
            var quote = await _tencent.GetAsync(code);
            var last = bars.Count > 0 ? bars[^1] : null;
            Console.WriteLine($"[DEBUG] code={code} lastDate={last?.Date:yyyy-MM-dd} today={DateTime.Today:yyyy-MM-dd} quote={quote?.Price}");
            if (quote != null && last != null)
            {
                if (last.Date.Date < DateTime.Today)
                {
                    bars.Add(new StockBar
                    {
                        Code = last.Code, Name = last.Name, Date = DateTime.Today,
                        Open = quote.Open, High = Math.Max(quote.Open, quote.Price),
                        Low = Math.Min(quote.Open, quote.Price),
                        Close = quote.Price, Volume = quote.Volume, Amount = last.Amount
                    });
                }
                else
                {
                    last.Close = quote.Price;
                    last.High = Math.Max(last.High, quote.Price);
                    last.Low = Math.Min(last.Low, quote.Price);
                }
            }
            return (bars, null);
        }

        return (null, "暂时无法获取系统行情数据，你可以上传CSV继续分析。");
    }
}
