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
        var reply = await _spark.ChatAsync(req.Message);
        return Json(new { reply });
    }
}

public record AskRequest(string Message);
