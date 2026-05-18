namespace StockAnalysis.Web.Services;

/// <summary>统一数据源结果，明确区分成功/失败，不允许吞异常</summary>
public record DataSourceResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }
    public string SourceName { get; init; } = "";

    public static DataSourceResult<T> Ok(T data, string source) => new()
    {
        Success = true,
        Data = data,
        SourceName = source
    };

    public static DataSourceResult<T> Fail(string error, string source) => new()
    {
        Success = false,
        Error = error,
        SourceName = source
    };
}
