namespace StockAnalysis.Core.Engines;

/// <summary>A股证券类型过滤器：只允许沪深主板普通A股进入机会池分析</summary>
public static class SecurityTypeFilter
{
    /// <summary>检查是否是普通A股（沪深主板），返回 (isValid, rejectReason)</summary>
    public static (bool IsValid, string? Reason) IsCommonAStock(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6)
            return (false, "代码长度不是6位");

        // 沪市主板
        if (code.StartsWith("600") || code.StartsWith("601") || code.StartsWith("603") || code.StartsWith("605"))
            return (true, null);

        // 深市主板
        if (code.StartsWith("000") || code.StartsWith("001") || code.StartsWith("002") || code.StartsWith("003"))
            return (true, null);

        // 创业板
        if (code.StartsWith("300") || code.StartsWith("301"))
            return (false, "创业板代码，跳过（适用不同交易规则）");

        // 科创板
        if (code.StartsWith("688") || code.StartsWith("689"))
            return (false, "科创板代码，跳过（适用不同交易规则）");

        // 北交所
        if (code.StartsWith("8") || code.StartsWith("4"))
            return (false, "北交所代码，跳过（适用不同交易规则）");

        // B股
        if (code.StartsWith("200") || code.StartsWith("900"))
            return (false, "B股代码，跳过");

        // ETF 代码（15/16/159/51 开头）
        if (code.StartsWith("15") || code.StartsWith("16"))
            return (false, "ETF/基金代码，跳过");

        // 深市 ETF/LOF（159/16 开头）
        if (code.StartsWith("159"))
            return (false, "ETF/LOF代码，跳过");

        // 沪市 ETF（510/511/512/513/515/516/517/518/519 开头）
        if (code.StartsWith("51") && code.Length == 6)
            return (false, "ETF代码，跳过");

        // 沪市其他基金（50/52/56/58 开头）
        if (code.StartsWith("50") || code.StartsWith("52") || code.StartsWith("56") || code.StartsWith("58"))
            return (false, "基金/REITs代码，跳过");

        // 债券/可转债（11/12/13 开头）
        if (code.StartsWith("11") || code.StartsWith("12") || code.StartsWith("13"))
            return (false, "债券/可转债代码，跳过");

        // 指数类
        if (code.StartsWith("39") || code.StartsWith("99"))
            return (false, "指数类代码，跳过");

        // 未识别代码：保守起见，仍然分析但标记
        return (true, null);
    }
}
