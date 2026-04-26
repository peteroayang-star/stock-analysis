using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public class ReportGenerator
{
    public void PrintSignals(List<StockSignal> signals)
    {
        foreach (var s in signals.OrderBy(x => x.Decision).ThenBy(x => x.RiskScore))
        {
            Console.WriteLine($"""

股票：{s.Code} {s.Name}  ({s.Date:yyyy-MM-dd})
决策：{s.Decision}
风险分：{s.RiskScore}
买点：{SignalName(s.SignalType)}
原因：{(s.Reasons.Count > 0 ? string.Join("、", s.Reasons) : "无")}
支撑位：{s.SupportPrice:F2}  止损位：{s.StopLossPrice:F2}  观察位：{s.WatchPrice:F2}
""");
        }
    }

    public void PrintBacktest(List<BacktestResult> results, BacktestSummary summary)
    {
        Console.WriteLine($"\n{"代码",-8}{"日期",-12}{"信号",-16}{"1日%",-8}{"3日%",-8}{"5日%",-8}{"10日%",-8}{"最大回撤%"}");
        Console.WriteLine(new string('-', 75));
        foreach (var r in results)
            Console.WriteLine($"{r.Code,-8}{r.SignalDate:yyyy-MM-dd}  {SignalName(r.SignalType),-16}{r.Return1D,-8:F2}{r.Return3D,-8:F2}{r.Return5D,-8:F2}{r.Return10D,-8:F2}{r.MaxDrawdown:F2}");

        Console.WriteLine($"""

=== 回测汇总 ===
信号数：{summary.TotalSignals}  胜率：{summary.WinRate:F1}%
5日均收益：{summary.AvgReturn5D:F2}%  盈亏比：{summary.ProfitLossRatio:F2}
平均盈利：{summary.AvgWin:F2}%  平均亏损：{summary.AvgLoss:F2}%
""");
    }

    // CSV 字段：Code,Name,Decision,RiskScore,SignalType,Reasons,SupportPrice,StopLossPrice,WatchPrice
    public void ExportCsv(List<StockSignal> signals, string outputPath)
    {
        var lines = new List<string> { "Code,Name,Decision,RiskScore,SignalType,Reasons,SupportPrice,StopLossPrice,WatchPrice" };
        lines.AddRange(signals.Select(s =>
            $"{s.Code},{s.Name},{s.Decision},{s.RiskScore},{SignalName(s.SignalType)}," +
            $"\"{string.Join("、", s.Reasons)}\",{s.SupportPrice:F2},{s.StopLossPrice:F2},{s.WatchPrice:F2}"));
        File.WriteAllLines(outputPath, lines, System.Text.Encoding.UTF8);
        Console.WriteLine($"已导出：{outputPath}");
    }

    private static string SignalName(BuySignalType t) => t switch
    {
        BuySignalType.VolumeBreakout  => "倍量突破",
        BuySignalType.PullbackSupport => "缩量回踩",
        BuySignalType.VolumeWashout   => "凹量洗盘",
        _ => "无信号"
    };
}
