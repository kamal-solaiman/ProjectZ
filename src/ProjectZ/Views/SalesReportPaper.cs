using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ProjectZ.Models;

namespace ProjectZ.Views;

/// <summary>A4 landscape with repeated headers. Wide tables continue in column bands; no off-screen grid screenshot.</summary>
public static class SalesReportPaper
{
    public const double Width=297*96/25.4, Height=210*96/25.4;
    private const double Pad=42, ContentWidth=Width-2*Pad;
    private static TextBlock Text(string text,double size=12,bool bold=false) => new() { Text=text, FontSize=size,
        Style=new Style(typeof(TextBlock)), FontFamily=new FontFamily("Segoe UI"), Foreground=Brushes.Black,
        FlowDirection=FlowDirection.RightToLeft, TextWrapping=TextWrapping.Wrap, FontWeight=bold ? FontWeights.Bold : FontWeights.Normal };
    private static Grid Line(IReadOnlyList<string> values,bool header)
    {
        var g=new Grid { Width=ContentWidth, FlowDirection=FlowDirection.RightToLeft, Background=header ? Brushes.Gainsboro : Brushes.White };
        for(int i=0;i<values.Count;i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var cell=new Border { BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(.5),Padding=new Thickness(5,3,5,3), Child=Text(values[i],12,header) };
            Grid.SetColumn(cell,i);g.Children.Add(cell);
        }
        return g;
    }
    public static FixedDocument Build(string store, SalesReportPage report, SalesReportTable table)
    {
        var document=new FixedDocument(); document.DocumentPaginator.PageSize=new Size(Width,Height);
        var bands=Enumerable.Range(1,table.Columns.Length-1).Chunk(5).Select(c=>new[]{0}.Concat(c).ToArray()).ToArray();
        for(int band=0;band<bands.Length;band++)
        {
            var cols=bands[band]; StackPanel body=null!; double used=0;
            void NewPage()
            {
                var page=new FixedPage { Width=Width, Height=Height, Background=Brushes.White, Language=XmlLanguage.GetLanguage("ar-EG") };
                body=new StackPanel { Width=ContentWidth, FlowDirection=FlowDirection.RightToLeft };
                FixedPage.SetLeft(body,Pad);FixedPage.SetTop(body,Pad);page.Children.Add(body);
                body.Children.Add(Text($"{store}  /  تقارير المبيعات",13,true));body.Children.Add(Text($"{report.Title}  •  {table.Title}",20,true));
                body.Children.Add(Text(report.Context,11));
                body.Children.Add(Text(string.Join("    |    ",report.Metrics.Select(m=>$"{m.Label}: {m.Display}")),11));
                if(report.Warning.Length>0)body.Children.Add(Text(report.Warning,11));
                body.Children.Add(Line(cols.Select(i=>table.Columns[i]).ToArray(),true));
                body.Measure(new Size(ContentWidth,double.PositiveInfinity));used=body.DesiredSize.Height;
                var foot=Text($"صفحة {document.Pages.Count+1}   •   مجموعة الأعمدة {band+1} / {bands.Length}   •   {table.Rows.Count} صف",11);
                FixedPage.SetBottom(foot,20);FixedPage.SetRight(foot,Pad);page.Children.Add(foot);
                var pc=new PageContent(); ((IAddChild)pc).AddChild(page);document.Pages.Add(pc);
            }
            NewPage();
            foreach(var row in table.Rows)
            {
                // Long names or notes continue on additional lines/pages, never elided from the paper.
                var parts=cols.Select(c=>Chunks(row.Display[c])).ToArray();
                for(int k=0;k<parts.Max(a=>a.Length);k++)
                {
                    var cells=parts.Select((a,i)=>k<a.Length ? a[k] : i==0 ? "↳ " + a[0] : "").ToArray();
                    var line=Line(cells,false);line.Measure(new Size(ContentWidth,double.PositiveInfinity));
                    if(used+line.DesiredSize.Height>Height-2*Pad)NewPage();
                    used+=line.DesiredSize.Height;body.Children.Add(line);
                }
            }
        }
        return document;
    }
    private static string[] Chunks(string value)
    {
        if(value.Length<=150)return [value];
        var text=new StringInfo(value); var result=new List<string>();
        for(int i=0;i<text.LengthInTextElements;i+=150) result.Add(text.SubstringByTextElements(i,Math.Min(150,text.LengthInTextElements-i)));
        return result.ToArray();
    }
}
