namespace StockAnalysis.Web.Services;

/// <summary>CSV模板服务：提供模板路径和格式说明</summary>
public class CsvTemplateService
{
    public static string TemplatePath => "/data/template.csv";

    public static string FormatDescription =>
        "列顺序：日期, 开盘, 最高, 最低, 收盘, 成交量, 成交额";

    public static string ExampleRow =>
        "2026-01-02,10.00,10.50,9.80,10.30,100000,1030000";
}
