namespace StockAnalysis.Core.Engines;

public class CandidateStockItem
{
    public string StockCode { get; set; } = "";
    public string StockName { get; set; } = "";
    public string Sector { get; set; } = "";
    public decimal LastScore { get; set; }
    public string LastDecision { get; set; } = "";
    public DateTime LastAnalyzeTime { get; set; }

    public decimal MainUpPlatformScore { get; set; }
    public decimal ResonanceScore { get; set; }
    public decimal UpsideSpaceScore { get; set; }
    public decimal ChipLockScore { get; set; }

    public int ConsecutiveAppearDays { get; set; }
    public bool IsPreviousWatchPool { get; set; }
    public bool IsPreviousValuePool { get; set; }

    /// <summary>扫描优先级分（越高越优先）</summary>
    public decimal ScanPriorityScore =>
        (IsPreviousValuePool ? 30 : 0)
        + (IsPreviousWatchPool ? 15 : 0)
        + Math.Min(ConsecutiveAppearDays * 5, 20)
        + MainUpPlatformScore * 0.15m
        + ChipLockScore * 0.10m
        + ResonanceScore * 0.10m;
}
