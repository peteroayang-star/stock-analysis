using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>决策排序器，按交易优先级对信号列表排序（Buy 最优先，Ignore 最末）</summary>
public class DecisionRanker
{
    /// <summary>将决策类型映射为排序优先级数字（越小越优先）</summary>
    private static int Priority(Decision d) => d switch
    {
        Decision.Buy    => 0,
        Decision.Watch  => 1,
        Decision.Hold   => 2,
        Decision.Reduce => 3,
        Decision.Sell   => 4,
        _               => 5
    };

    /// <summary>
    /// 对信号列表按决策优先级排序，同优先级内按风险分升序
    /// </summary>
    /// <param name="signals">待排序的信号列表</param>
    /// <returns>排序后的新列表</returns>
    public List<StockSignal> Rank(List<StockSignal> signals) =>
        signals.OrderBy(s => Priority(s.Decision)).ThenBy(s => s.RiskScore).ToList();
}
