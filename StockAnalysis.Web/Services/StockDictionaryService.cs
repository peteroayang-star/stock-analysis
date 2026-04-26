using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>股票字典：代码 ↔ 名称映射，以及本地已收录股票列表</summary>
public class StockDictionaryService
{
    private readonly Dictionary<string, string> _codeToName = new()
    {
        ["000001"] = "平安银行",
        ["600519"] = "贵州茅台",
        ["000858"] = "五粮液",
        ["601318"] = "中国平安",
        ["000333"] = "美的集团"
    };

    private readonly Dictionary<string, string> _nameToCode;

    public StockDictionaryService()
    {
        _nameToCode = _codeToName.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    public string? GetCode(string input) =>
        _codeToName.ContainsKey(input) ? input : _nameToCode.GetValueOrDefault(input);

    public string? GetName(string code) => _codeToName.GetValueOrDefault(code);

    public IReadOnlyCollection<string> ListNames() => _codeToName.Values;
}
