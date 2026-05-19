using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Models;

public static class RiskTagDisplay
{
    public static string CssClass(RiskTag tag) => tag.Severity switch
    {
        3 => "bg-danger",
        2 => "bg-warning text-dark",
        _ => "bg-secondary"
    };
}

public class SignalWithSuggestion
{
    public StockSignal Signal { get; set; } = null!;
    public string Suggestion { get; set; } = "";
    public RealTimeQuote? RealTime { get; set; }
    public FinanceData? Finance { get; set; }
    public int FinanceRiskAdj { get; set; }
    public List<string> FinanceReasons { get; set; } = [];
}

public class AnalysisViewModel
{
    public List<SignalWithSuggestion> Items { get; set; } = [];
    public TradingMode Mode { get; set; } = TradingMode.Candidate;
}

public class BacktestViewModel
{
    public List<BacktestResult> Results { get; set; } = [];
    public BacktestSummary? Summary { get; set; }
}

public class WatchPoolItem
{
    public int Rank { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? MarketCap { get; set; }
    public string Sector { get; set; } = "";
    public decimal WatchPoolScore { get; set; }
    public decimal SecondWaveProbability { get; set; }
    public decimal LockPositionStrength { get; set; }
    public decimal ChipLockScore { get; set; }
    public string SectorEmotion { get; set; } = "";
    public int RiskScore { get; set; }
    public Decision Decision { get; set; }
    public string Reason { get; set; } = "";
    public string RiskWarning { get; set; } = "";
    public string Tier { get; set; } = ""; // 激进/重点/普通
    public decimal ResonanceScore { get; set; }
    public int SectorRisingCount { get; set; }
    public int SectorLimitUpCount { get; set; }
    public bool IsIndependentPump { get; set; }
    public bool IsFakeBreakoutRisk { get; set; }
    public bool IsValuePool { get; set; }
    public decimal SectorHeatScore { get; set; }
    public bool IsMainstreamSector { get; set; }
    public bool IsSectorDeclining { get; set; }
    public string SectorRotationNote { get; set; } = "";
    public List<RiskTag> RiskTags { get; set; } = [];
    public bool HasHighRisk => RiskTags.Any(t => t.Severity >= 2);
    public string RiskTagSummary => string.Join("; ", RiskTags.Select(t => t.Label));
    // 龙头地位
    public LeaderRole LeaderRole { get; set; }
    public int SectorRank { get; set; }
    public string SectorEmotionLabel { get; set; } = "";
    public bool IsMarketLeader { get; set; }
    public string LeaderReason { get; set; } = "";

    // 显示标签（替代数字评分）
    public string ScoreLabel => WatchPoolScore >= 80 ? "A" : WatchPoolScore >= 68 ? "B" : "C";
    public string ScoreStrength => WatchPoolScore >= 80 ? "强" : WatchPoolScore >= 68 ? "中" : "弱";
    public string RiskLabel => RiskScore <= 30 ? "低" : RiskScore <= 50 ? "中" : "高";
    public string RiskColor => RiskScore <= 30 ? "#10B981" : RiskScore <= 50 ? "#F59E0B" : "#EF4444";
    public bool HasSectorData => !string.IsNullOrEmpty(Sector) && SectorEmotionLabel != "-";
    public string LeaderDisplay => LeaderRole == LeaderRole.Edge && !HasSectorData ? "" :
        LeaderRoleDisplay.Label(LeaderRole);
    public string SectorDisplay => HasSectorData ? Sector : "";
}

public static class LeaderRoleDisplay
{
    public static string Label(LeaderRole r) => r switch
    {
        LeaderRole.Leader   => "龙头",
        LeaderRole.Core     => "中军",
        LeaderRole.Follower => "跟风",
        LeaderRole.CatchUp  => "补涨",
        LeaderRole.Edge     => "普通",
        _ => ""
    };
    public static string BadgeClass(LeaderRole r) => r switch
    {
        LeaderRole.Leader   => "bg-danger",
        LeaderRole.Core     => "bg-warning text-dark",
        LeaderRole.Follower => "bg-info",
        LeaderRole.CatchUp  => "bg-success",
        _                   => "bg-secondary"
    };
}

public class WatchPoolResult
{
    public DateTime Date { get; set; } = DateTime.Today;
    public List<WatchPoolItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int ScannedCount { get; set; }
    public int FailedCount { get; set; }
    public int FilteredCount { get; set; }
    public int MatchedCount { get; set; }
    public int RemainingCount => TotalCount - ScannedCount - FailedCount - FilteredCount;
    public string? CurrentStockCode { get; set; }
    public string? CurrentStockName { get; set; }
    public double ElapsedSeconds { get; set; }
    public double EstimatedRemainingSeconds { get; set; }
    public List<MainstreamSectorResult> Top10Sectors { get; set; } = [];
    public List<SectorRotationResult> SectorRotations { get; set; } = [];
    // 数据源状态
    public string DataSourceName { get; set; } = "AKShare";
    public int DataSourceFailCount { get; set; }
    public bool UsingCache { get; set; }
    public int SkippedCount { get; set; }
    public List<string> DataSourceWarnings { get; set; } = [];
    // 市场上下文
    public List<MainlineSector> MainlineSectors { get; set; } = [];
    public MarketEmotionCycle MarketEmotion { get; set; }
    public string MarketSummary { get; set; } = "";
    public string MarketEmotionLabel => MarketEmotion switch
    {
        MarketEmotionCycle.Launch  => "启动",
        MarketEmotionCycle.Ferment => "发酵",
        MarketEmotionCycle.Climax  => "高潮",
        MarketEmotionCycle.Diverge => "分歧",
        MarketEmotionCycle.Decline => "退潮",
        _ => "-"
    };
    public bool HasSectorData => MainlineSectors.Count > 0;

    // 市场广度数据
    public int MarketRisingCount { get; set; }
    public int MarketFallingCount { get; set; }
    public int MarketLimitUpCount { get; set; }
    public int MarketLimitDownCount { get; set; }
    public decimal AvgGain { get; set; }
    public string MarketBreadthLabel => AvgGain switch {
        >= 0.03m => "极强", >= 0.01m => "偏强", >= -0.005m => "中性", >= -0.02m => "偏弱", _ => "极弱"
    };
    public string MarketBreadthColor => AvgGain switch {
        >= 0.03m => "#10B981", >= 0.01m => "#34D399", >= -0.005m => "#9CA3AF", >= -0.02m => "#F59E0B", _ => "#EF4444"
    };
    // 数据源状态（紧凑）
    public string DataSourceStatusLabel => UsingCache ? "本地缓存" : DataSourceFailCount > 0 ? "部分异常" : "实时";
    public string DataSourceStatusColor => UsingCache ? "#F59E0B" : DataSourceFailCount > 0 ? "#EF4444" : "#10B981";
}

/// <summary>机会池扫描进度（供前端轮询）</summary>
public record ScanProgress(
    int Processed, int Total, int Matched, int Filtered, int Failed,
    string CurrentStock, string CurrentSector, string CurrentSource,
    int FailedSources, string Status, string? Message, DateTime UpdatedAt)
{
    public bool IsDone => Status == "Completed" || Status == "Failed";
    public int Percent => Total > 0 ? (int)((double)Processed / Total * 100) : 0;
}

public class ScreenerResultItem
{
    public DragonScreener.DragonResult Dragon { get; set; } = null!;
    public StockSignal Signal { get; set; } = null!;
    public string Suggestion { get; set; } = "";
    public RealTimeQuote? RealTime { get; set; }
    public bool IsMainUpPlatform { get; set; }
    public decimal? LockPositionStrength { get; set; }
    public decimal? SecondWaveProbability { get; set; }
    public string? SectorEmotionLabel { get; set; }
    public decimal? ChipLockScore { get; set; }
}

