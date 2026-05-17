namespace StockAnalysis.Web.Services;

/// <summary>财报数据风险调整（共享逻辑）</summary>
public static class FinanceHelper
{
    /// <summary>根据最新季度净利润/营收同比计算风险调整值</summary>
    public static (int RiskAdj, List<string> Reasons) CalculateRiskAdjustment(FinanceData? fin)
    {
        if (fin == null || fin.ProfitYoy.Length == 0)
            return (0, []);

        var reasons = new List<string>();
        int adj = 0;

        // 净利润同比
        var profitYoy = Math.Abs(fin.ProfitYoy[0]) > 1000 ? double.NaN : fin.ProfitYoy[0];
        if (!double.IsNaN(profitYoy))
        {
            if (profitYoy < -20) { adj += 20; reasons.Add($"净利润同比下滑{Math.Abs(profitYoy):F1}%"); }
            else if (profitYoy < 0) { adj += 10; reasons.Add($"净利润同比下滑{Math.Abs(profitYoy):F1}%"); }
            else if (profitYoy > 30) { adj -= 10; reasons.Add($"净利润同比增长{profitYoy:F1}%"); }
        }

        // 营收同比
        if (fin.RevenueYoy.Length > 0)
        {
            var revenueYoy = Math.Abs(fin.RevenueYoy[0]) > 1000 ? double.NaN : fin.RevenueYoy[0];
            if (!double.IsNaN(revenueYoy))
            {
                if (revenueYoy < -10) { adj += 10; reasons.Add($"营收同比下滑{Math.Abs(revenueYoy):F1}%"); }
                else if (revenueYoy > 20) { adj -= 5; reasons.Add($"营收同比增长{revenueYoy:F1}%"); }
            }
        }

        return (adj, reasons);
    }
}
