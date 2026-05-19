using System.Text;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>
/// 交易逻辑引擎：从交易视角生成公司逻辑摘要，非F10资料页。
/// 输出高度浓缩的"为什么涨、有没有逻辑、能不能持续、风险在哪里"。
/// </summary>
public class TradingLogicEngine
{
    /// <summary>
    /// 分析交易逻辑。fin 为 null 时财报相关字段使用默认值。
    /// </summary>
    public TradingLogicResult Analyze(StockSignal signal, string? sectorName = null,
        double[]? revenueYoy = null, double[]? profitYoy = null,
        decimal? marketCap = null)
    {
        var result = new TradingLogicResult();

        var sectorInfo = LookupSector(sectorName);
        result.MainBusiness = sectorInfo.Business;
        result.CoreProduct = sectorInfo.Product;
        result.CoreConcept = sectorInfo.Concept;
        result.IndustryChain = sectorInfo.Chain;

        // 市场属性
        result.MarketAttribute = DetermineMarketAttribute(signal.StockStyle, marketCap);

        // 资金风格
        result.CapitalStyle = DetermineCapitalStyle(signal.StockStyle, signal.SmartMoney);

        // 财报摘要
        result.FinancialSummary = BuildFinancialSummary(revenueYoy, profitYoy);

        // 当前炒作逻辑
        result.HypeLogic = ClassifyHypeLogic(signal, sectorInfo);

        // 持续性
        result.Sustainability = EvaluateSustainability(signal);

        // 风险点
        result.RiskPoints = CollectRiskPoints(signal, revenueYoy, profitYoy);

        // 交易逻辑摘要
        result.TradingSummary = BuildTradingSummary(result, signal, sectorName);

        return result;
    }

    // ═══════════════════════════════════════════════
    // 板块 → 产业链映射
    // ═══════════════════════════════════════════════

    private static (string Business, string Product, string Concept, string Chain) LookupSector(string? sector)
    {
        if (string.IsNullOrEmpty(sector))
            return ("—", "—", "—", "—");

        var s = sector.ToLower();
        // 半导体/芯片
        if (s.Contains("半导体") || s.Contains("芯片") || s.Contains("集成电路") || s.Contains("封测"))
            return ("半导体材料与器件", "芯片设计/制造/封测", "国产替代 + AI算力", "半导体产业链中上游");
        // AI / 算力
        if (s.Contains("算力") || s.Contains("ai") || s.Contains("人工智能") || s.Contains("大模型") || s.Contains("gpu"))
            return ("AI算力基础设施", "算力服务器/光模块/GPU", "AI算力 + 大模型训练", "AI产业链基础设施层");
        // 机器人
        if (s.Contains("机器人") || s.Contains("自动化") || s.Contains("减速器") || s.Contains("伺服"))
            return ("工业自动化与机器人", "减速器/伺服电机/控制器", "具身智能 + 工业机器人", "智能装备产业链");
        // 新能源
        if (s.Contains("锂电") || s.Contains("电池") || s.Contains("储能") || s.Contains("新能源"))
            return ("新能源与储能", "锂电池/储能系统/光伏", "能源转型 + 储能需求", "新能源产业链中游");
        // 光伏
        if (s.Contains("光伏") || s.Contains("太阳能"))
            return ("光伏发电", "硅片/电池片/组件", "光伏出海 + 技术迭代", "光伏产业链");
        // 汽车
        if (s.Contains("汽车") || s.Contains("整车") || s.Contains("智驾") || s.Contains("自动驾驶"))
            return ("智能汽车与零部件", "整车/智驾系统/零部件", "智能驾驶 + 国产替代", "汽车产业链");
        // 医药
        if (s.Contains("医药") || s.Contains("制药") || s.Contains("生物") || s.Contains("cro") || s.Contains("创新药"))
            return ("医药健康", "创新药/医疗器械/CRO", "创新药出海 + 老龄化", "医药产业链");
        // 消费
        if (s.Contains("消费") || s.Contains("食品") || s.Contains("白酒") || s.Contains("家电"))
            return ("消费品", "食品饮料/家电/零售", "消费复苏 + 品牌升级", "消费产业链");
        // 软件 / 信创
        if (s.Contains("软件") || s.Contains("信创") || s.Contains("数据") || s.Contains("安全"))
            return ("信息技术与软件", "基础软件/信息安全/数据服务", "信创替代 + 数据要素", "软件与信息服务产业链");
        // 军工
        if (s.Contains("军工") || s.Contains("航天") || s.Contains("航空"))
            return ("国防军工", "航空航天/军工电子/舰船", "国防装备 + 军民融合", "军工产业链");
        // 金融
        if (s.Contains("金融") || s.Contains("银行") || s.Contains("券商") || s.Contains("保险"))
            return ("金融服务", "银行/券商/保险", "资本市场 + 金融改革", "金融服务业");
        // 通信
        if (s.Contains("通信") || s.Contains("5g") || s.Contains("6g") || s.Contains("光模块") || s.Contains("光纤"))
            return ("通信设备与服务", "光通信/基站/网络设备", "5G/6G + 算力网络", "通信产业链");
        // 电力
        if (s.Contains("电力") || s.Contains("电网") || s.Contains("特高压"))
            return ("电力与电网", "发电/输配电/特高压", "电力改革 + 新能源并网", "电力产业链");
        // 周期/资源
        if (s.Contains("化工") || s.Contains("有色") || s.Contains("钢铁") || s.Contains("煤炭") || s.Contains("稀土"))
            return ("资源与材料", "化工品/有色金属/特种材料", "周期复苏 + 资源稀缺", "上游资源材料");

        // 默认
        return ("制造业/服务业", "核心产品", sector, "相关产业链");
    }

    // ═══════════════════════════════════════════════
    // 市场属性
    // ═══════════════════════════════════════════════

    private static string DetermineMarketAttribute(StockStyle style, decimal? marketCap)
    {
        var size = marketCap switch
        {
            < 50 => "小盘",
            < 200 => "中盘",
            >= 200 => "大盘",
            _ => "中盘"
        };
        var attr = style switch
        {
            StockStyle.TrendInstitutional => "成长趋势",
            StockStyle.EmotionSpeculative => "题材活跃",
            StockStyle.LargeCapVolume => "价值蓝筹",
            StockStyle.BottomLaunch => "底部反转",
            StockStyle.DistributionDecline => "衰退调整",
            _ => "一般"
        };
        return $"{size}·{attr}";
    }

    // ═══════════════════════════════════════════════
    // 资金风格
    // ═══════════════════════════════════════════════

    private static string DetermineCapitalStyle(StockStyle style, SmartMoneyBehavior smartMoney)
    {
        if (style == StockStyle.EmotionSpeculative)
            return "游资情绪驱动";
        if (style == StockStyle.TrendInstitutional)
            return "机构趋势资金";
        if (style == StockStyle.LargeCapVolume)
            return "机构配置型资金";

        return smartMoney switch
        {
            SmartMoneyBehavior.AggressiveAttack => "主动进攻型资金",
            SmartMoneyBehavior.Accumulation => "资金低吸建仓",
            SmartMoneyBehavior.Distribution or SmartMoneyBehavior.Dumping => "资金流出/减仓",
            _ => "资金温和参与"
        };
    }

    // ═══════════════════════════════════════════════
    // 财报摘要
    // ═══════════════════════════════════════════════

    private static string BuildFinancialSummary(double[]? revenueYoy, double[]? profitYoy)
    {
        if (revenueYoy == null || profitYoy == null || revenueYoy.Length == 0)
            return "暂无最新财报数据";

        var rev = revenueYoy[0];
        var prf = profitYoy[0];

        var parts = new List<string>();

        if (!double.IsNaN(prf) && Math.Abs(prf) < 1000)
        {
            if (prf > 30) parts.Add("利润大幅增长");
            else if (prf > 10) parts.Add("利润稳健增长");
            else if (prf > 0) parts.Add("利润小幅增长");
            else if (prf > -20) parts.Add("利润有所下滑");
            else parts.Add("利润明显下滑");
        }

        if (!double.IsNaN(rev) && Math.Abs(rev) < 1000)
        {
            if (rev > 20) parts.Add("营收快速增长");
            else if (rev > 5) parts.Add("营收稳定增长");
            else if (rev > -10) parts.Add("营收基本持平");
            else parts.Add("营收出现收缩");
        }

        if (parts.Count == 0)
            parts.Add("财务数据异常或缺失");

        // 总体判断
        var status = DetermineFinancialStatus(revenueYoy, profitYoy);
        parts.Add($"整体财务状态：{status}");

        return string.Join("，", parts);
    }

    private static string DetermineFinancialStatus(double[]? revenueYoy, double[]? profitYoy)
    {
        if (profitYoy == null || profitYoy.Length == 0)
            return "数据不足";

        double sum = 0; int count = 0;
        foreach (var p in profitYoy.Take(4))
        {
            if (!double.IsNaN(p) && Math.Abs(p) < 1000)
            { sum += p; count++; }
        }
        if (count == 0) return "数据不足";

        double avg = sum / count;
        if (avg > 20) return "改善";
        if (avg > 0) return "稳定";
        if (avg > -20) return "转弱";
        return "承压";
    }

    // ═══════════════════════════════════════════════
    // 炒作逻辑分类
    // ═══════════════════════════════════════════════

    private static string ClassifyHypeLogic(StockSignal s, (string Business, string Product, string Concept, string Chain) sector)
    {
        // 情绪龙头
        if (s.IsEmotionLeader)
            return "情绪炒作 — 作为板块情绪龙头，连板资金驱动，短线博弈特征明显";

        // 情绪活跃
        if (s.IsEmotionActive)
            return "概念刺激 — 板块情绪升温，题材催化为主，关注情绪持续性";

        // 主线平台
        if (s.MainUpPlatform?.IsMainUpPlatform == true && s.MainUpPlatform.SecondWaveProbability >= 60)
            return "主线驱动 — 处于主线板块中，平台整理后二波预期，板块资金持续回流";

        // 机构趋势
        if (s.StockStyle == StockStyle.TrendInstitutional && s.Trend == Trend.Up
            && s.DetailedStage is DetailedTrendStage.MainUpEarly or DetailedTrendStage.MainUpMid)
            return "机构趋势 — 趋势机构型资金推动，均线多头排列，走势稳健";

        // 底部反转
        if (s.StockStyle == StockStyle.BottomLaunch)
            return "周期反转 — 底部放量异动，可能为资金左侧布局，需确认趋势延续";

        // 业绩驱动
        if (s.RiskScore <= 30 && s.Trend == Trend.Up && s.StockStyle == StockStyle.LargeCapVolume)
            return "业绩驱动 — 基本面改善预期，机构配置型资金流入，估值修复逻辑";

        // 趋势跟踪
        if (s.Trend == Trend.Up)
            return "趋势跟踪 — 价格沿均线上行，趋势惯性较好，关注趋势拐点信号";

        // 技术反弹
        if (s.SignalType != BuySignalType.None)
            return "技术信号 — 技术形态出现买入结构，但需结合板块和市场环境判断";

        // 默认
        return "个股博弈 — 缺乏明确主线逻辑驱动，关注后续催化因素";
    }

    // ═══════════════════════════════════════════════
    // 持续性评估
    // ═══════════════════════════════════════════════

    private static string EvaluateSustainability(StockSignal s)
    {
        var score = 0;

        // 趋势健康
        if (s.Trend == Trend.Up) score += 2;
        else if (s.Trend == Trend.Sideways) score += 1;

        // 风险可控
        if (s.RiskScore <= 30) score += 2;
        else if (s.RiskScore <= 50) score += 1;

        // 位置合理
        if (s.DetailedStage is DetailedTrendStage.MainUpEarly or DetailedTrendStage.MainUpMid) score += 2;
        else if (s.DetailedStage is DetailedTrendStage.TrendBuilding or DetailedTrendStage.Launch) score += 1;
        else if (s.DetailedStage is DetailedTrendStage.MainUpLate or DetailedTrendStage.HighShock) score -= 1;

        // 量价健康
        if (s.VolumeDescription.Contains("放量") && !s.VolumeDescription.Contains("滞涨")) score += 1;
        if (s.VolumeDescription.Contains("缩量")) score += 0;

        // 过度延伸
        if (s.IsOverextended) score -= 2;

        // 情绪过热
        if (s.IsEmotionLeader) score -= 1;

        return score switch
        {
            >= 5 => "逻辑支撑较强，趋势健康，持续性较好",
            >= 3 => "逻辑部分成立，需观察后续催化与资金动向",
            >= 1 => "逻辑偏弱，短期博弈为主，持续性存疑",
            _ => "风险较高，持续性较差，不宜追涨"
        };
    }

    // ═══════════════════════════════════════════════
    // 风险点收集
    // ═══════════════════════════════════════════════

    private static List<string> CollectRiskPoints(StockSignal s, double[]? revenueYoy, double[]? profitYoy)
    {
        var points = new List<string>();

        // 高位风险
        if (s.IsOverextended)
            points.Add("高位偏离 — 当前价格大幅偏离均线，追涨风险较高");
        else if (s.DetailedStage is DetailedTrendStage.MainUpLate or DetailedTrendStage.HighShock)
            points.Add("高位震荡 — 处于高位博弈区，分歧加大");

        // 情绪风险
        if (s.IsEmotionLeader)
            points.Add("高位情绪 — 作为情绪龙头，一旦情绪退潮回调幅度可能较大");
        if (s.LimitUpCountIn14Days >= 3)
            points.Add("高换手 — 短期涨停频次高，筹码交换剧烈");

        // 趋势风险
        if (s.TrendRisk >= 60)
            points.Add("趋势转弱 — 均线结构松动，趋势可靠性下降");
        if (s.SupportBroken)
            points.Add("支撑破位 — 已跌破MA20关键支撑");

        // 波动风险
        if (s.VolatilityRisk >= 60)
            points.Add("波动加剧 — 短期振幅扩大，持仓体验较差");

        // 财务风险
        if (profitYoy != null && profitYoy.Length > 0)
        {
            if (!double.IsNaN(profitYoy[0]) && profitYoy[0] < -30)
                points.Add("业绩波动 — 利润同比大幅下滑，基本面承压");
        }

        // 估值风险
        if (s.PositionLevel == PositionLevel.High && s.RiskScore >= 50)
            points.Add("估值偏高 — 位置高位叠加风险较高，安全边际不足");

        // 情绪退潮风险
        if (s.SentimentRisk >= 60)
            points.Add("情绪退潮 — 市场情绪转弱，板块热度可能下降");

        // 无明确风险
        if (points.Count == 0)
            points.Add("当前无明显极端风险信号");

        return points;
    }

    // ═══════════════════════════════════════════════
    // 交易逻辑摘要 (100-200字)
    // ═══════════════════════════════════════════════

    private static string BuildTradingSummary(TradingLogicResult r, StockSignal s, string? sectorName)
    {
        var sb = new StringBuilder();

        // 第1句：公司定位
        if (!string.IsNullOrEmpty(r.MainBusiness) && r.MainBusiness != "—")
            sb.Append($"公司属于{r.MainBusiness}");
        if (!string.IsNullOrEmpty(r.CoreConcept) && r.CoreConcept != "—")
            sb.Append($"（{r.CoreConcept}）");
        if (!string.IsNullOrEmpty(sectorName))
            sb.Append($"方向。");
        else
            sb.Append("。");

        // 第2句：驱动逻辑
        if (s.Trend == Trend.Up)
        {
            if (s.MainUpPlatform?.IsMainUpPlatform == true)
                sb.Append("近期板块资金持续回流，平台整理后有望二波；");
            else if (s.IsEmotionLeader)
                sb.Append("当前为板块情绪龙头，短线博弈资金主导；");
            else
                sb.Append("趋势向上，资金温和参与；");
        }
        else if (s.Trend == Trend.Sideways)
            sb.Append("当前处于震荡整理阶段，方向待选择；");

        // 第3句：财务/基本面
        var finStatus = DetermineFinancialStatus(
            r.FinancialSummary.Contains("增长") ? new[] { 30.0 } : r.FinancialSummary.Contains("下滑") ? new[] { -30.0 } : new[] { 5.0 },
            null);
        if (r.FinancialSummary.Contains("增长"))
            sb.Append("财务表现改善，利润同比增长。");
        else if (r.FinancialSummary.Contains("下滑") || r.FinancialSummary.Contains("收缩"))
            sb.Append("财务表现有所转弱，需关注基本面变化。");
        else
            sb.Append("财务表现相对稳定。");

        // 第4句：风险提示
        if (s.RiskScore >= 50)
            sb.Append("当前风险偏高，需要注意高位分歧和回调风险。");
        else if (s.IsOverextended)
            sb.Append("短期涨幅较大，偏离均线较远，注意追高风险。");
        else if (s.IsEmotionLeader)
            sb.Append("情绪博弈特征明显，注意情绪退潮后的流动性风险。");
        else
            sb.Append("整体风险可控，关注趋势延续性。");

        var summary = sb.ToString();
        // 确保在100-200字范围
        if (summary.Length > 200)
            summary = summary[..200];
        if (summary.Length < 50)
            summary += " 建议持续跟踪板块动态和基本面变化。";

        return summary;
    }
}
