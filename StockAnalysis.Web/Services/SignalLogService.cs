using System.Text.Json;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

public record SignalLogEntry(
    DateTime LogTime, string Code, string Name, DateTime SignalDate,
    decimal Close, string Decision, int RiskScore, int PositionPct,
    string ActionAdvice, decimal? StopLossPrice, decimal? TargetPrice);

public class SignalLogService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public SignalLogService(IWebHostEnvironment env)
        => _path = Path.Combine(env.ContentRootPath, "signal_log.json");

    public void Append(IEnumerable<StockSignal> signals)
    {
        var entries = Load();
        foreach (var s in signals)
            entries.Add(new SignalLogEntry(DateTime.Now, s.Code, s.Name, s.Date,
                s.Close, s.Decision.ToString(), s.RiskScore, s.PositionPct,
                s.ActionAdvice, s.StopLossPrice, s.TargetPrice));
        File.WriteAllText(_path, JsonSerializer.Serialize(entries, _opts));
    }

    public List<SignalLogEntry> Load()
    {
        if (!File.Exists(_path)) return [];
        try { return JsonSerializer.Deserialize<List<SignalLogEntry>>(File.ReadAllText(_path)) ?? []; }
        catch { return []; }
    }
}
