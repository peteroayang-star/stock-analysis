namespace StockAnalysis.Core.Models;

public enum SeatType { HotMoney, Institution, Quant, Retail, Unknown }

public record DragonTigerSeat(
    string SeatName,
    SeatType SeatType,
    decimal Amount,
    bool IsFamousHotMoney,
    bool IsRepeatedSeat
);

public record DragonTigerRecord(
    string StockCode,
    string StockName,
    DateTime TradeDate,
    decimal BuyAmount,
    decimal SellAmount,
    decimal NetBuyAmount,
    List<DragonTigerSeat> BuySeats,
    List<DragonTigerSeat> SellSeats
);
