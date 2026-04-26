using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

public class LocalStockDataService
{
    private readonly DataImporter _importer;
    private readonly string _dataPath;
    private readonly Dictionary<string, string> _stockMap = new()
    {
        ["000001"] = "平安银行",
        ["平安银行"] = "000001",
        ["600519"] = "贵州茅台",
        ["贵州茅台"] = "600519",
        ["000858"] = "五粮液",
        ["五粮液"] = "000858",
        ["601318"] = "中国平安",
        ["中国平安"] = "601318",
        ["000333"] = "美的集团",
        ["美的集团"] = "000333"
    };

    public LocalStockDataService(DataImporter importer, IWebHostEnvironment env)
    {
        _importer = importer;
        _dataPath = Path.Combine(env.WebRootPath, "data");
    }

    public List<StockBar>? TryGetStock(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (!_stockMap.TryGetValue(input, out var mapped)) return null;

        var code = input.Length == 6 ? input : mapped;
        var name = input.Length == 6 ? mapped : input;
        var file = Path.Combine(_dataPath, $"{code}.csv");

        return File.Exists(file) ? _importer.ImportCsv(file, code, name) : null;
    }
}
