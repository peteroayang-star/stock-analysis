using Microsoft.AspNetCore.Mvc;

namespace StockAnalysis.Web.Controllers;

public class SettingsController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
