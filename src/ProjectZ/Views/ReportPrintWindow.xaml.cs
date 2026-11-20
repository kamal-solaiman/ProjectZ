using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ProjectZ.Views;

public partial class ReportPrintWindow : Window
{
    private readonly string section;
    public ReportPrintWindow(FixedDocument paper, string section)
    {
        InitializeComponent();
        this.section = section;
        Title = $"{ProjectZ.Services.StoreIdentity.Current.Name} | معاينة {section}";
        Viewer.Document = paper;
    }
    private void Print_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var dialog=new PrintDialog(); if(dialog.ShowDialog()!=true)return;
            dialog.PrintTicket.PageMediaSize=new PageMediaSize(PageMediaSizeName.ISOA4);
            dialog.PrintTicket.PageOrientation=PageOrientation.Landscape;
            dialog.PrintDocument(Viewer.Document.DocumentPaginator,section);
        }
        catch(Exception ex) { MessageBox.Show(this,"تعذرت الطباعة: "+ex.Message,Title,MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
}
