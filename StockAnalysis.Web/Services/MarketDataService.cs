using StockAnalysis.Core.Engines;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Web.Services;

/// <summary>统一数据入口：本地内置 → AKShare缓存/接口 → 失败返回null</summary>
public class MarketDataService
{
    private readonly DataImporter _importer;
    private readonly AkShareDataService _akShare;
    private readonly string _builtinPath;

    public MarketDataService(DataImporter importer, AkShareDataService akShare, IWebHostEnvironment env)
    {
        _importer = importer;
        _akShare = akShare;
        _builtinPath = Path.Combine(env.WebRootPath, "data");
    }

    public async Task<(List<StockBar>? Bars, string? Error)> TryGetBarsAsync(string input)
    {
        var (code, name) = await _akShare.ResolveAsync(input);
        if (code == null)
            return (null, "未能识别该股票，请输入6位代码或正确的股票名称");

        // 内置数据优先
        var builtinFile = Path.Combine(_builtinPath, $"{code}.csv");
        if (File.Exists(builtinFile))
            return (_importer.ImportCsv(builtinFile, code, name ?? code), null);

        // AKShare
        var bars = await _akShare.TryGetBarsAsync(code, name ?? code);
        if (bars != null) return (bars, null);

        return (null, "暂时无法获取系统行情数据，你可以上传CSV继续分析。");
    }
}
