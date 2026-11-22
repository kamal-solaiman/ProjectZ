using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectZ.Models;

namespace ProjectZ.Services;

public static class ReportWorkbook
{
    public static void Save(string path,string store,ReportPage report)
    {
        using var book=new XLWorkbook();
        var summary=book.AddWorksheet("الملخص"); summary.RightToLeft=true;
        summary.Cell(1,1).Value=store; summary.Cell(1,2).Value=report.Section; summary.Cell(2,1).Value=report.Title; summary.Cell(3,1).Value=report.Context;
        summary.Cell(4,1).Value=report.Warning; summary.Column(1).Width=42; summary.Column(2).Width=20; summary.Column(3).Width=15;
        summary.Cell(6,1).Value="المؤشر"; summary.Cell(6,2).Value="القيمة"; summary.Cell(6,3).Value="الوحدة"; Header(summary.Range(6,1,6,3));
        int r=7;
        foreach(var m in report.Metrics) { summary.Cell(r,1).Value=m.Label; Write(summary.Cell(r,2),m.Value); summary.Cell(r,3).Value=m.Unit; r++; }
        foreach(var table in report.Tables)
        {
            if(table.Rows.Count>=1048576) throw new InvalidOperationException("تجاوز الجدول سعة ورقة Excel. اختر فترة أقصر.");
            var sheet=book.AddWorksheet(table.Title); sheet.RightToLeft=true; sheet.SheetView.FreezeRows(1);
            for(var col=0;col<table.Columns.Length;col++) { sheet.Cell(1,col+1).Value=table.Columns[col]; sheet.Column(col+1).Width=col==0 ? 27 : 22; }
            Header(sheet.Range(1,1,1,table.Columns.Length));
            r=2; foreach(var row in table.Rows) { for(var col=0;col<row.Cells.Length;col++) Write(sheet.Cell(r,col+1),row.Cells[col]); r++; }
            if(r>2)sheet.Range(1,1,r-1,table.Columns.Length).SetAutoFilter();
        }
        // A failure while generating the workbook must not destroy an existing file chosen by the operator.
        var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,Guid.NewGuid()+".xlsx");
        try { book.SaveAs(temp); File.Move(temp,path,true); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    private static void Header(IXLRange range) { range.Style.Font.Bold=true; range.Style.Fill.BackgroundColor=XLColor.FromHtml("#173C56"); range.Style.Font.FontColor=XLColor.White; }
    private static void Write(IXLCell cell,object? value)
    {
        switch(value)
        {
            case null: break; // missing monetary evidence is a blank, not a numeric zero
            // Excel only preserves 15 significant digits. Never lose cents on exceptionally large amounts.
            case decimal n when Math.Abs(n) >= 10_000_000_000_000m:
                cell.Value=n.ToString("0.00",CultureInfo.InvariantCulture); cell.Style.NumberFormat.Format="@"; break;
            case decimal n: cell.Value=(double)n; cell.Style.NumberFormat.Format="#,##0.00;[Red]-#,##0.00;0.00"; break;
            case int n: cell.Value=n; break;
            case long n: cell.Value=(double)n; break;
            case DateOnly day: cell.Value=day.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format="yyyy-mm-dd"; break;
            case DateTime dt: cell.Value=dt; cell.Style.DateFormat.Format="yyyy-mm-dd hh:mm"; break;
            default: cell.Value=value.ToString() ?? ""; break; // text, never FormulaA1 (including leading =, + and barcodes)
        }
    }
}
