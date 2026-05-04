using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

    public async Task<string?> ChatAsync(string userMessage)
    {
        var body = new
        {
            model = _model,
            messages = new[] { new { role = "user", content = userMessage } },
            stream = false
        };

        var req = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{_apiKey}:{_apiSecret}");

        var resp = await _http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"[SPARK RAW] {json}");
        try
        {
            var doc = JsonDocument.Parse(json).RootElement;
            return doc.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex)
        {
            return $"[错误] {ex.Message} | 原始响应: {json}";
        }
    }
}
