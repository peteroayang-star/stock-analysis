using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>市场主线识别结果</summary>
public record MarketContextResult(
    List<MainlineSector> MainlineSectors,
    MarketEmotionCycle EmotionCycle,
    string MarketSummary
);

/// <summary>主线板块</summary>
public record MainlineSector(
    string SectorName,
    decimal HeatScore,
    int StockCount,
    int LimitUpCount,
    int RisingCount,
    decimal TotalTurnover, // 板块总成交(亿)
    decimal ContinuityScore, // 持续性
    MarketEmotionCycle EmotionCycle, // 该板块的情绪周期
    string LeaderStock,    // 龙头股代码
    string LeaderStockName
);

/// <summary>市场情绪周期</summary>
public enum MarketEmotionCycle
{
    Launch,    // 启动
    Ferment,   // 发酵
    Climax,    // 高潮
    Diverge,   // 分歧
    Decline    // 退潮
}

/// <summary>龙头角色</summary>
public enum LeaderRole
{
    Leader,      // 龙头
    Core,        // 中军
    Follower,    // 跟风
    CatchUp,     // 补涨
    Edge         // 边缘
}

/// <summary>个股在板块中的龙头地位</summary>
public record StockLeaderPosition(
    LeaderRole Role,
    int RankInSector,          // 板块内排名
    decimal SectorHeat,        // 所属板块热度
    string SectorName,         // 所属板块
    string SectorEmotionLabel, // 板块情绪标签
    bool IsMarketLeader,       // 是否为市场总龙头
    string LeaderReason
);

public class MarketContextEngine
{
    /// <summary>从市场统计识别主线和情绪周期</summary>
    public MarketContextResult Analyze(
        int totalStocks, int risingCount, int limitUpCount, decimal avgChangePct,
        List<(string SectorName, MainstreamSectorResult Heat)> sectorResults)
    {
        if (totalStocks == 0)
            return new MarketContextResult([], MarketEmotionCycle.Decline, "数据不足");

        // ── 1. 全市场情绪 ────────────────────
        int total = totalStocks;
        int rising = risingCount;
        int limitUpAll = limitUpCount;
        decimal avgGain = avgChangePct;

        MarketEmotionCycle marketEmotion = avgGain switch
        {
            > 3m when limitUpAll >= 30 => MarketEmotionCycle.Climax,
            > 1.5m when limitUpAll >= 15 => MarketEmotionCycle.Ferment,
            > 0.5m when rising > total * 0.5 => MarketEmotionCycle.Launch,
            < -2m => MarketEmotionCycle.Decline,
            < 0 when rising < total * 0.3 => MarketEmotionCycle.Diverge,
            _ => MarketEmotionCycle.Launch
        };

        string emotionLabel = marketEmotion switch
        {
            MarketEmotionCycle.Launch   => "启动",
            MarketEmotionCycle.Ferment  => "发酵",
            MarketEmotionCycle.Climax   => "高潮",
            MarketEmotionCycle.Diverge  => "分歧",
            MarketEmotionCycle.Decline  => "退潮",
            _ => "-"
        };

        // ── 2. 主线板块排序 ──────────────────
        var mainlines = sectorResults
            .OrderByDescending(s => s.Heat.SectorHeatScore)
            .Take(10)
            .Select(s =>
            {
                // 板块情绪周期
                var secEmotion = s.Heat.SectorHeatScore >= 80 ? MarketEmotionCycle.Climax
                    : s.Heat.SectorHeatScore >= 65 ? MarketEmotionCycle.Ferment
                    : s.Heat.IsDeclining ? MarketEmotionCycle.Decline
                    : MarketEmotionCycle.Launch;

                return new MainlineSector(
                    SectorName: s.SectorName,
                    HeatScore: s.Heat.SectorHeatScore,
                    StockCount: 0, // TODO: 从板块成分股接口获取
                    LimitUpCount: s.Heat.LimitUpCount,
                    RisingCount: s.Heat.RisingCount,
                    TotalTurnover: 0,
                    ContinuityScore: s.Heat.SectorContinuity,
                    EmotionCycle: secEmotion,
                    LeaderStock: "",
                    LeaderStockName: ""
                );
            })
            .ToList();

        string summary = marketEmotion switch
        {
            MarketEmotionCycle.Climax => $"市场高潮，{mainlines.Count}个主线板块，涨停{limitUpAll}只，注意分歧风险",
            MarketEmotionCycle.Ferment => $"市场发酵，{mainlines.Count}个主线板块，赚钱效应扩散",
            MarketEmotionCycle.Launch => $"市场启动，{mainlines.Count}个热点板块",
            MarketEmotionCycle.Diverge => $"市场分歧，上涨{rising}/{total}，谨慎追高",
            MarketEmotionCycle.Decline => $"市场退潮，上涨{rising}/{total}，防守为主",
            _ => ""
        };

        return new MarketContextResult(mainlines, marketEmotion, summary);
    }

    /// <summary>识别个股在板块中的龙头地位</summary>
    public static StockLeaderPosition IdentifyLeaderRole(
        StockSignal signal, decimal stockAmount, decimal stockChangePct,
        MainlineSector? sector, int sectorRank)
    {
        if (sector == null)
            return new StockLeaderPosition(LeaderRole.Edge, 0, 0, "", "-", false, "无板块数据");

        var role = LeaderRole.Edge;
        var reasons = new List<string>();

        // 龙头：成交额板块前3 + 有涨停 + 板块带动性
        var amountRank = sectorRank <= 3;
        var hasLimitUp = signal.LimitUpCountIn14Days >= 1 ||
            (stockChangePct >= 9.5m);

        if (amountRank && hasLimitUp && signal.PositionLevel != PositionLevel.High)
        {
            role = LeaderRole.Leader;
            reasons.Add("成交额前排+涨停+位置安全");
        }
        else if (amountRank && signal.IsEmotionActive)
        {
            role = LeaderRole.Core;
            reasons.Add("成交额前排+情绪活跃");
        }
        else if (!amountRank && hasLimitUp)
        {
            role = LeaderRole.Follower;
            reasons.Add("后排涨停");
        }
        else if (signal.RiskScore <= 40 && signal.DetailedStage == DetailedTrendStage.MainUpEarly)
        {
            role = LeaderRole.CatchUp;
            reasons.Add("低位补涨潜质");
        }
        else
        {
            role = LeaderRole.Edge;
            reasons.Add("板块边缘");
        }

        return new StockLeaderPosition(
            Role: role,
            RankInSector: sectorRank,
            SectorHeat: sector.HeatScore,
            SectorName: sector.SectorName,
            SectorEmotionLabel: sector.EmotionCycle switch
            {
                MarketEmotionCycle.Launch => "启动", MarketEmotionCycle.Ferment => "发酵",
                MarketEmotionCycle.Climax => "高潮", MarketEmotionCycle.Diverge => "分歧",
                MarketEmotionCycle.Decline => "退潮", _ => "-"
            },
            IsMarketLeader: role == LeaderRole.Leader && sector.HeatScore >= 80,
            LeaderReason: string.Join(";", reasons)
        );
    }
}
