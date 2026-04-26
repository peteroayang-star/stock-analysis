using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>持仓组合分析器，对多只持仓股票批量执行持仓模式分析</summary>
public class PortfolioAnalyzer
{
    private readonly StockAnalyzer _analyzer;

    /// <param name="cfg">应用配置</param>
    public PortfolioAnalyzer(AppConfig cfg) => _analyzer = new StockAnalyzer(cfg);

    /// <summary>
    /// 分析持仓列表，返回每只股票的持仓决策
    /// </summary>
    /// <param name="positions">持仓记录列表</param>
    /// <param name="allBars">所有股票的 K 线数据</param>
    /// <returns>每只持仓股票的分析结果</returns>
    public List<StockSignal> Analyze(List<PortfolioPosition> positions, List<StockBar> allBars)
    {
        var results = new List<StockSignal>();
        foreach (var pos in positions)
        {
            var bars = allBars.Where(b => b.Code == pos.Code).OrderBy(b => b.Date).ToList();
            results.AddRange(_analyzer.Analyze(bars, TradingMode.Portfolio));
        }
        return results;
    }
}
