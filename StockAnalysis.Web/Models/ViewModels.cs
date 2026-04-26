using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Models;

public class SignalWithSuggestion
{
    public StockSignal Signal { get; set; } = null!;
    public string Suggestion { get; set; } = "";
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
