using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StockAnalysis.Web.Models;

namespace StockAnalysis.Web.Services;

public class SparkAiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _model;
    private readonly string _url;

    public SparkAiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey    = config["Spark:ApiKey"] ?? "";
        _apiSecret = config["Spark:ApiSecret"] ?? "";
        _model     = config["Spark:Model"] ?? "generalv3.5";
        _url       = config["Spark:Url"] ?? "https://spark-api-open.xf-yun.com/v1/chat/completions";
    }

    /// <summary>基于结构化摘要生成 150-300 字解读（禁止传原始K线数组）</summary>
    public async Task<string?> GenerateAnalysisAsync(StructuredAnalysisResult data, int maxLen = 300)
    {
        var prompt = BuildAnalysisPrompt(data);
        var result = await ChatWithSystem(prompt,
            "你是一位专业的A股分析摘要助手。根据提供的结构化分析数据，生成一段150-300字的中文市场行为解读。" +
            "要求：客观描述当前市场状态，解释量价关系和风险特征，给出清晰的结论。" +
            "禁止使用'必涨'、'抄底'、'清仓'、'庄家控盘'等极端建议用语。不要重复原始数据，而是给出有洞察力的解读。");
        if (string.IsNullOrWhiteSpace(result)) return null;

        // 截断到 maxLen 汉字边界
        if (result.Length > maxLen)
        {
            var cut = result[..maxLen];
            var lastPeriod = cut.LastIndexOf('。');
            if (lastPeriod > maxLen / 2) cut = cut[..(lastPeriod + 1)];
            return cut;
        }
        return result;
    }

    private static string BuildAnalysisPrompt(StructuredAnalysisResult d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"股票: {d.StockName}({d.StockCode})");
        sb.AppendLine($"趋势: {d.TrendState} | 量价: {d.VolumeState} | 分时: {d.IntradayState}");
        sb.AppendLine($"风险: {d.RiskLevel}({d.RiskScore}分) | 趋势风险{d.TrendRisk}/波动风险{d.VolatilityRisk}/情绪风险{d.SentimentRisk}");
        sb.AppendLine($"决策: {d.Decision} | 周期: {d.CycleStage}");
        sb.AppendLine($"关键价格: 支撑{d.SupportPrice:F2} 止损{d.StopLossPrice:F2} 观察{d.WatchPrice:F2}" +
            (d.TargetPrice.HasValue ? $" 目标{d.TargetPrice:F2}" : ""));
        if (!string.IsNullOrEmpty(d.SectorName))
            sb.AppendLine($"板块: {d.SectorName} | 情绪: {d.SectorEmotion ?? "-"}");
        if (!string.IsNullOrEmpty(d.SmartMoneyDescription))
            sb.AppendLine($"主力: {d.SmartMoneyDescription}");
        if (!string.IsNullOrEmpty(d.MainUpPlatformSummary))
            sb.AppendLine($"平台: {d.MainUpPlatformSummary}");
        if (d.IsEmotionLeader) sb.AppendLine("注意: 该股为当前情绪龙头");
        if (d.LimitUpCountIn14Days > 0) sb.AppendLine($"14天内涨停: {d.LimitUpCountIn14Days}次");
        sb.AppendLine($"操作建议: {d.ActionAdvice}");
        return sb.ToString();
    }

    /// <summary>保留原有 ChatAsync 方法，供其他场景使用</summary>
    public async Task<string?> ChatAsync(string userMessage)
    {
        return await ChatWithSystem(userMessage,
            "你是一位专业的A股技术分析助手，擅长K线形态、均线系统、量价关系和风险控制。请用简洁专业的语言回答用户的股票分析问题。");
    }

    private async Task<string?> ChatWithSystem(string userMessage, string systemPrompt)
    {
        var body = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            stream = false
        };

        var req = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{_apiKey}:{_apiSecret}");

        var resp = await _http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        try
        {
            var doc = JsonDocument.Parse(json).RootElement;
            return doc.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex)
        {
            return $"[错误] {ex.Message}";
        }
    }
}
