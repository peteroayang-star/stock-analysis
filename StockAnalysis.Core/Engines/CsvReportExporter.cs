using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>CSV 报告导出器，将分析结果导出为中文字段的 CSV 文件</summary>
public class CsvReportExporter
{
    /// <summary>将信号类型枚举转换为中文名称</summary>
    private static string SignalName(BuySignalType t) => t switch
    {
        BuySignalType.VolumeBreakout  => "倍量突破",
        BuySignalType.PullbackSupport => "缩量回踩",
        BuySignalType.VolumeWashout   => "凹量洗盘",
        _                             => "无信号"
    };

    /// <summary>
    /// 导出分析结果到 CSV 文件
    /// </summary>
    /// <param name="signals">信号列表</param>
    /// <param name="path">输出文件路径</param>
    public void Export(List<StockSignal> signals, string path)
    {
        var lines = new List<string>
        {
            "代码,名称,日期,收盘价,决策,风险分,信号类型,原因,支撑位,止损位,观察位"
        };
        lines.AddRange(signals.Select(s =>
            $"{s.Code},{s.Name},{s.Date:yyyy-MM-dd},{s.Close:F2},{s.Decision}," +
            $"{s.RiskScore},{SignalName(s.SignalType)}," +
            $"\"{string.Join("、", s.Reasons)}\"," +
            $"{s.SupportPrice:F2},{s.StopLossPrice:F2},{s.WatchPrice:F2}"));
        File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
    }
}
