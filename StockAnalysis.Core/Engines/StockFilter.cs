using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>股票过滤器，排除不符合条件的股票（ST、科创板、流动性不足等）</summary>
public class StockFilter
{
    private readonly FilterConfig _cfg;

    /// <param name="cfg">过滤条件配置</param>
    public StockFilter(FilterConfig cfg) => _cfg = cfg;

    /// <summary>
    /// 判断是否应排除该股票
    /// </summary>
    /// <param name="code">股票代码</param>
    /// <param name="name">股票名称</param>
    /// <param name="bars">K 线列表</param>
    /// <returns>排除原因字符串；返回 null 表示通过过滤</returns>
    public string? ShouldExclude(string code, string name, List<StockBar> bars)
    {
        if (name.StartsWith("ST") || name.StartsWith("*ST")) return "ST股";
        if (code.StartsWith("688")) return "科创板";
        if (code.StartsWith("300") || code.StartsWith("301")) return "创业板";
        if (code.StartsWith("8")) return "北交所";
        if (bars.Count < _cfg.MinListedDays) return $"上市不足{_cfg.MinListedDays}日";

        var lastBar = bars[^1];
        if (lastBar.Amount < (decimal)(_cfg.MinAmountMillionYuan * 1_000_000))
            return $"成交额不足{_cfg.MinAmountMillionYuan}百万";

        return null;
    }
}
