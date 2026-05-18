using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>统一数据入口：ProviderRouter → 本地CSV → AKShare → 腾讯实时补充</summary>
public class MarketDataService
{
    private readonly AkShareDataService _akShare;
    private readonly ProviderRouter _router;

    public MarketDataService(AkShareDataService akShare, ProviderRouter router)
    {
        _akShare = akShare;
        _router = router;
    }

    public Task<List<MinuteBar>?> TryGetMinuteBarsAsync(string code, DateTime? expectedDate = null)
        => _akShare.TryGetMinuteBarsAsync(code, expectedDate);

    public async Task<(List<StockBar>? Bars, string? Error)> TryGetBarsAsync(string input)
    {
        var (code, name) = await _akShare.ResolveAsync(input);
        if (code == null)
            return (null, "未能识别该股票，请输入6位代码或正确的股票名称");

        // 通过 ProviderRouter 按优先级获取历史K线（LocalCSV → AKShare）
        var barResult = await _router.GetBarsAsync(code);
        if (!barResult.Success || barResult.Data == null)
            return (null, barResult.Error ?? "暂时无法获取系统行情数据，你可以上传CSV继续分析。");

        var bars = barResult.Data;
        // 用腾讯/新浪实时接口补充今日数据
        var quoteResult = await _router.GetRealtimeAsync(code);
        var quote = quoteResult.Success ? quoteResult.Data : null;
        var last = bars.Count > 0 ? bars[^1] : null;
        if (quote != null && last != null)
        {
            if (last.Date.Date < DateTime.Today)
            {
                bars.Add(new StockBar
                {
                    Code = last.Code, Name = last.Name, Date = DateTime.Today,
                    Open = quote.Open,
                    High = quote.High > 0 ? quote.High : Math.Max(quote.Open, quote.Price),
                    Low = quote.Low > 0 ? quote.Low : Math.Min(quote.Open, quote.Price),
                    Close = quote.Price, Volume = quote.Volume, Amount = quote.Amount > 0 ? quote.Amount : last.Amount
                });
            }
            else
            {
                last.Close = quote.Price;
                last.High = Math.Max(last.High, quote.High > 0 ? quote.High : quote.Price);
                last.Low = Math.Min(last.Low, quote.Low > 0 ? quote.Low : quote.Price);
            }
        }
        return (bars, null);
    }
}
