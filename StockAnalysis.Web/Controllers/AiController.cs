using Microsoft.AspNetCore.Mvc;
using StockAnalysis.Web.Services;

namespace StockAnalysis.Web.Controllers;

public class AiController : Controller
{
    private readonly SparkAiService _spark;
    public AiController(SparkAiService spark) => _spark = spark;

    [HttpGet] public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest();

        var systemPrompt = BuildSystemPrompt();
        var fullMessage = $"{systemPrompt}\n\n用户问题：{req.Message}";
        var reply = await _spark.ChatAsync(fullMessage);
        return Json(new { reply });
    }

    private string BuildSystemPrompt()
    {
        return @"你是【AI量价关系拆解助手】，不是股票喊单AI。

核心任务：通过量价关系+时间节奏+相对强弱，帮助用户理解当前市场行为。

严格禁止使用以下用词：
- 紧急清仓、主力跑路、庄家控盘、百分百上涨、必涨、爆拉、暴涨启动、错过拍大腿等极端情绪化表达

输出要求：
1. 描述【当前市场状态】而非下命令（如：放量突破阶段、缩量整理阶段、高位分歧阶段）
2. 必须解释【为什么】（基于成交量、均线、相对强弱等客观数据）
3. 分析量价关系（放量上涨=新增资金参与、缩量上涨=抛压有限、放量滞涨=高位分歧）
4. 保持专业、冷静、有逻辑，类似交易员复盘风格

你的目标是让用户【看懂市场行为】，而不是【依赖AI喊单】。";
    }
}

public record AskRequest(string Message);
