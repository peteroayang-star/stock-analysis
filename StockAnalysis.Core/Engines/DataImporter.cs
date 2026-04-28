using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using OfficeOpenXml;
using StockAnalysis.Core.Models;

namespace StockAnalysis.Core.Engines;

/// <summary>行情数据导入器，支持通达信 CSV 和 Excel 格式</summary>
public class DataImporter
{
    /// <summary>
    /// 导入通达信导出的 CSV 文件
    /// CSV 列顺序：日期, 开盘, 最高, 最低, 收盘, 成交量, 成交额
    /// </summary>
    /// <param name="filePath">CSV 文件路径</param>
    /// <param name="code">股票代码</param>
    /// <param name="name">股票名称</param>
    /// <returns>按日期升序排列的 K 线列表</returns>
    public List<StockBar> ImportCsv(string filePath, string code, string name)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null
        };

        using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        var bars = new List<StockBar>();
        csv.Read(); csv.ReadHeader();

        while (csv.Read())
        {
            if (!DateTime.TryParseExact(csv.GetField(0), new[]{"yyyyMMdd","yyyy-MM-dd","yyyy/MM/dd"},
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            bars.Add(new StockBar
            {
                Code = code, Name = name, Date = date,
                Open   = csv.GetField<decimal>(1),
                High   = csv.GetField<decimal>(2),
                Low    = csv.GetField<decimal>(3),
                Close  = csv.GetField<decimal>(4),
                Volume = (long)csv.GetField<double>(5),
                Amount = csv.GetField<decimal>(6)
            });
        }

        return bars.OrderBy(b => b.Date).ToList();
    }

    /// <summary>
    /// 导入 Excel 文件，每个 Sheet 对应一只股票
    /// Sheet 名称作为股票代码，第一行为表头，列顺序：日期, 开盘, 最高, 最低, 收盘, 成交量, 成交额
    /// </summary>
    /// <param name="filePath">Excel 文件路径</param>
    /// <returns>所有 Sheet 合并后按日期升序排列的 K 线列表</returns>
    public List<StockBar> ImportExcel(string filePath)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage(new FileInfo(filePath));
        var bars = new List<StockBar>();

        foreach (var sheet in package.Workbook.Worksheets)
        {
            var code = sheet.Name;
            for (int row = 2; row <= sheet.Dimension.End.Row; row++)
            {
                var dateStr = sheet.Cells[row, 1].Text;
                if (!DateTime.TryParseExact(dateStr, new[]{"yyyyMMdd","yyyy-MM-dd","yyyy/MM/dd"},
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                bars.Add(new StockBar
                {
                    Code = code, Name = code, Date = date,
                    Open   = decimal.Parse(sheet.Cells[row, 2].Text),
                    High   = decimal.Parse(sheet.Cells[row, 3].Text),
                    Low    = decimal.Parse(sheet.Cells[row, 4].Text),
                    Close  = decimal.Parse(sheet.Cells[row, 5].Text),
                    Volume = long.Parse(sheet.Cells[row, 6].Text),
                    Amount = decimal.Parse(sheet.Cells[row, 7].Text)
                });
            }
        }

        return bars.OrderBy(b => b.Date).ToList();
    }
}
