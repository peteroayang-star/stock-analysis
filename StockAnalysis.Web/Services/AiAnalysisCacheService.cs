using System.Collections.Concurrent;
using System.Text.Json;

namespace StockAnalysis.Web.Services;

/// <summary>AI 分析结果缓存：同一股票同一交易日只调用一次 AI，避免重复消耗 token</summary>
public class AiAnalysisCacheService
{
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly string _persistPath;

    public AiAnalysisCacheService(IWebHostEnvironment env)
    {
        _persistPath = Path.Combine(env.ContentRootPath, "..", "Data", "ai_cache.json");
        Load();
    }

    private static string Key(string code, DateTime date) => $"{code}_{date:yyyyMMdd}";

    public string? Get(string code, DateTime date)
    {
        _cache.TryGetValue(Key(code, date), out var val);
        return val;
    }

    public void Set(string code, DateTime date, string text)
    {
        var key = Key(code, date);
        _cache[key] = text;

        // 最多 500 条，超出时清理旧条目
        if (_cache.Count > 500)
        {
            var oldest = _cache.Keys.OrderBy(k => k).Take(100).ToList();
            foreach (var k in oldest) _cache.TryRemove(k, out _);
        }
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_persistPath)) return;
            var json = File.ReadAllText(_persistPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
                foreach (var kv in dict) _cache.TryAdd(kv.Key, kv.Value);
        }
        catch { /* 缓存文件损坏则忽略 */ }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_persistPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var dict = _cache.ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(_persistPath,
                JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { /* 持久化失败不影响功能 */ }
    }
}
