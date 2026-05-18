namespace StockAnalysis.Core.Models;

public enum RiskTagType
{
    LowPriceSurge,
    PreviouslyST,
    EarningsDecline,
    ConsecutiveLimitUps,
    AnnouncementRisk,
    PoorLiquidity
}

public record RiskTag(RiskTagType Type, string Label, int Severity, string Description);
// Severity: 1=注意, 2=警告, 3=危险
