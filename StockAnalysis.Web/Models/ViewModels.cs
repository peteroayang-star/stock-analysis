using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Models;

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

