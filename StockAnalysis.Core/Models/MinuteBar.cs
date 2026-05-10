namespace StockAnalysis.Core.Models;

public class MinuteBar
{
    public DateTime Time   { get; set; }
    public decimal  Open   { get; set; }
    public decimal  High   { get; set; }
    public decimal  Low    { get; set; }
    public decimal  Close  { get; set; }
    public long     Volume { get; set; }
}
