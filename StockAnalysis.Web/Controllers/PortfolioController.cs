using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;
using StockAnalysis.Web.Models;

namespace StockAnalysis.Web.Controllers;

public class PortfolioController : Controller
{
    private readonly StockAnalyzer _analyzer;
    private readonly RiskReasonAnalyzer _reasoner = new();
    private readonly DecisionRanker _ranker = new();

    public PortfolioController(StockAnalyzer analyzer) => _analyzer = analyzer;

    public IActionResult Index()
    {
        var bars = GenerateDemoBars("000001", "平安银行", 60);
        var signals = _analyzer.Analyze(bars, TradingMode.Candidate);
        var ranked = _ranker.Rank(signals);
        var items = ranked.Select(s => new SignalWithSuggestion
        {
            Signal = s,
            Suggestion = _reasoner.Suggestion(s.SignalType, s.Decision, s.Reasons)
        }).ToList();
        return View("Index", new AnalysisViewModel { Items = items, Mode = TradingMode.Candidate });
    }

    private static List<StockBar> GenerateDemoBars(string code, string name, int days)
    {
        var rng = new Random(42);
        var bars = new List<StockBar>();
        decimal close = 10m;
        for (int i = 0; i < days; i++)
        {
            var change = (decimal)(rng.NextDouble() * 0.06 - 0.02);
            var open = close;
            close = Math.Round(open * (1 + change), 2);
            var high = Math.Round(Math.Max(open, close) * (1 + (decimal)(rng.NextDouble() * 0.02)), 2);
            var low  = Math.Round(Math.Min(open, close) * (1 - (decimal)(rng.NextDouble() * 0.02)), 2);
            var vol  = rng.Next(500000, 5000000);
            bars.Add(new StockBar { Code = code, Name = name, Date = DateTime.Today.AddDays(i - days + 1),
                Open = open, High = high, Low = low, Close = close, Volume = vol, Amount = vol * close });
        }
        return bars;
    }
}
