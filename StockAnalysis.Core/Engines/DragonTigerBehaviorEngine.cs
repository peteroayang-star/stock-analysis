using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

public record DragonTigerBehaviorResult(
    bool IsOnDragonTigerList,
    int ListedDaysIn10,
    int ConsecutiveListedDays,
    decimal NetBuyAmount,
    decimal NetBuyRatio,
    decimal InstitutionBuyRatio,
    decimal HotMoneyBuyRatio,
    bool IsHotMoneyRelay,
    bool IsInstitutionEntering,
    bool IsRepeatedSeat,
    bool IsOneDayTour,
    bool IsLockPosition,
    decimal SpeculationStrength,
    decimal RelayStrength,
    List<string> Tags,
    string Summary);

public class DragonTigerBehaviorEngine
{
    private static readonly DragonTigerBehaviorResult _empty =
        new(false, 0, 0, 0, 0, 0, 0, false, false, false, false, false, 0, 0, [], "暂无龙虎榜数据");

    public DragonTigerBehaviorResult Analyze(List<DragonTigerRecord>? records)
    {
        if (records == null || records.Count == 0) return _empty;

        var recent = records.Where(r => r.TradeDate >= DateTime.Today.AddDays(-10)).ToList();
        if (recent.Count == 0) return _empty;

        int listedDays = recent.Count;

        // 连续上榜天数
        var dates = recent.Select(r => r.TradeDate.Date).OrderByDescending(d => d).ToList();
        int consecutive = 1;
        for (int i = 1; i < dates.Count; i++)
        {
            if ((dates[i - 1] - dates[i]).TotalDays <= 3) consecutive++;
            else break;
        }

        decimal netBuy   = recent.Sum(r => r.NetBuyAmount);
        decimal totalBuy = recent.Sum(r => r.BuyAmount);
        decimal totalSell = recent.Sum(r => r.SellAmount);
        decimal netRatio = (totalBuy + totalSell) > 0 ? netBuy / (totalBuy + totalSell) : 0;

        var allBuySeats = recent.SelectMany(r => r.BuySeats).ToList();
        decimal instRatio = totalBuy > 0
            ? allBuySeats.Where(s => s.SeatType == SeatType.Institution).Sum(s => s.Amount) / totalBuy : 0;
        decimal hotRatio = totalBuy > 0
            ? allBuySeats.Where(s => s.SeatType == SeatType.HotMoney).Sum(s => s.Amount) / totalBuy : 0;

        bool hotRelay = allBuySeats.Where(s => s.IsFamousHotMoney)
            .GroupBy(s => s.SeatName).Any(g => g.Count() >= 2);
        bool instEntering = instRatio >= 0.3m && netRatio > 0;
        bool repeated = listedDays >= 3;
        bool oneDayTour = listedDays == 1 && netRatio < 0;
        bool lockPos = consecutive >= 2 && netRatio > 0.3m;

        decimal specStrength = Math.Clamp(hotRatio * 60 + (hotRelay ? 20 : 0) + (listedDays >= 3 ? 20 : 0), 0, 100);
        decimal relayStrength = Math.Clamp((hotRelay ? 40 : 0) + (lockPos ? 30 : 0) + (netRatio > 0.5m ? 30 : 0), 0, 100);

        var tags = new List<string>();
        if (hotRelay)     tags.Add("游资接力");
        if (instEntering) tags.Add("机构进场");
        if (oneDayTour)   tags.Add("一日游风险");
        if (lockPos)      tags.Add("锁仓特征");

        string summary = oneDayTour   ? "龙虎榜一日游风险，主力资金次日可能撤退，谨慎追高" :
                         hotRelay     ? $"游资接力明显，近10日上榜{listedDays}次，短线情绪较强" :
                         instEntering ? "机构席位参与，资金质量较高" :
                                        $"近10日上榜{listedDays}次，净买入{(netRatio >= 0 ? "为正" : "为负")}";

        return new DragonTigerBehaviorResult(
            true, listedDays, consecutive, netBuy, netRatio,
            instRatio, hotRatio, hotRelay, instEntering,
            repeated, oneDayTour, lockPos,
            specStrength, relayStrength, tags, summary);
    }
}
