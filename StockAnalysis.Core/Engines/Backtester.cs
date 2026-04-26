using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>单次回测结果</summary>
/// <param name="Code">股票代码</param>
/// <param name="Name">股票名称</param>
/// <param name="SignalDate">信号触发日期</param>
/// <param name="SignalType">信号类型</param>
/// <param name="EntryPrice">买入价（信号当日收盘价）</param>
/// <param name="Return1D">1日后收益率（%）</param>
/// <param name="Return3D">3日后收益率（%）</param>
/// <param name="Return5D">5日后收益率（%）</param>
/// <param name="Return10D">10日后收益率（%）</param>
/// <param name="MaxDrawdown">持有期间最大回撤（%）</param>
public record BacktestResult(
    string Code, string Name, DateTime SignalDate,
    BuySignalType SignalType, decimal EntryPrice,
    decimal Return1D, decimal Return3D, decimal Return5D, decimal Return10D,
    decimal MaxDrawdown
);

/// <summary>回测汇总统计</summary>
/// <param name="TotalSignals">信号总数</param>
/// <param name="WinRate">5日胜率（%）</param>
/// <param name="AvgReturn5D">5日平均收益率（%）</param>
/// <param name="AvgWin">盈利交易平均收益（%）</param>
/// <param name="AvgLoss">亏损交易平均亏损（%）</param>
/// <param name="ProfitLossRatio">盈亏比（AvgWin / AvgLoss）</param>
public record BacktestSummary(
    int TotalSignals, decimal WinRate,
    decimal AvgReturn5D, decimal AvgWin, decimal AvgLoss, decimal ProfitLossRatio
);

/// <summary>回测引擎，遍历历史 K 线检测信号并统计收益</summary>
public class Backtester
{
    private readonly BuySignalDetector _detector;

    /// <param name="cfg">信号检测参数配置</param>
    public Backtester(SignalConfig cfg) => _detector = new BuySignalDetector(cfg);

    /// <summary>
    /// 对 K 线列表执行回测，每个信号点记录后续 1/3/5/10 日收益
    /// </summary>
    /// <param name="bars">K 线列表（按日期升序）</param>
    /// <returns>每个信号的回测结果列表</returns>
    public List<BacktestResult> Run(List<StockBar> bars)
    {
        var results = new List<BacktestResult>();
        for (int i = 19; i < bars.Count - 10; i++)
        {
            var (signalType, _) = _detector.Detect(bars, i);
            if (signalType == BuySignalType.None) continue;

            var entry = bars[i].Close;
            decimal ret(int n) => (bars[i + n].Close - entry) / entry * 100;
            var maxDD = (entry - bars.Skip(i + 1).Take(10).Min(b => b.Low)) / entry * 100;

            results.Add(new BacktestResult(
                bars[i].Code, bars[i].Name, bars[i].Date, signalType, entry,
                ret(1), ret(3), ret(5), ret(10), maxDD));
        }
        return results;
    }

    /// <summary>
    /// 汇总回测结果，计算胜率、盈亏比等统计指标
    /// </summary>
    /// <param name="results">回测结果列表</param>
    /// <returns>汇总统计</returns>
    public BacktestSummary Summarize(List<BacktestResult> results)
    {
        if (results.Count == 0) return new BacktestSummary(0, 0, 0, 0, 0, 0);
        var wins   = results.Where(r => r.Return5D > 0).ToList();
        var losses = results.Where(r => r.Return5D <= 0).ToList();
        decimal avgWin  = wins.Count   > 0 ? wins.Average(r => r.Return5D)              : 0;
        decimal avgLoss = losses.Count > 0 ? Math.Abs(losses.Average(r => r.Return5D)) : 0;
        return new BacktestSummary(
            results.Count, (decimal)wins.Count / results.Count * 100,
            results.Average(r => r.Return5D), avgWin, avgLoss,
            avgLoss == 0 ? 0 : avgWin / avgLoss);
    }
}
