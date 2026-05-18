using Microsoft.AspNetCore.Mvc;

namespace StockAnalysis.Web.Controllers;

/// <summary>AI 解读已迁移至个股详情页的"生成AI解读"按钮</summary>
public class AiController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Stock");
    }
}
