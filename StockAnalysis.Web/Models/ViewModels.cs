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

public class ScreenerResultItem
{
    public DragonScreener.DragonResult Dragon { get; set; } = null!;
    public StockSignal Signal { get; set; } = null!;
    public string Suggestion { get; set; } = "";
    public RealTimeQuote? RealTime { get; set; }
}

