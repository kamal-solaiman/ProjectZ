using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class SalesReportsView : UserControl
{
    private SalesReportsViewModel? Vm=>DataContext as SalesReportsViewModel;
    public SalesReportsView()
    {
        InitializeComponent(); DataContextChanged+=(_,e)=> {
            if(e.OldValue is SalesReportsViewModel old)old.PropertyChanged-=Changed;
            if(Vm is not null)Vm.PropertyChanged+=Changed;
            Columns(); };
    }
    private void Changed(object? sender,PropertyChangedEventArgs e) { if(e.PropertyName==nameof(SalesReportsViewModel.SelectedTable))Columns(); }
    private void Columns()
    {
        Results.Columns.Clear(); if(Vm?.SelectedTable is not {} table)return;
        for(int i=0;i<table.Columns.Length;i++) Results.Columns.Add(new DataGridTextColumn { Header=table.Columns[i], Binding=new Binding($"Display[{i}]") { Mode=BindingMode.OneWay } });
        Results.FrozenColumnCount=1;
    }
    private async void Preset_Click(object sender,RoutedEventArgs e)
    { if(Vm is {} vm && sender is Button {Tag:string days} && int.TryParse(days,out var n)) await vm.PresetAsync(n); }
    private void Results_DoubleClick(object sender,MouseButtonEventArgs e)
    { if(e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(Results,origin) is DataGridRow && Vm is {} vm)vm.DrillCommand.Execute(null); }
    private void FlushFilters()
    {
        DocumentSearch.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DetailSearch.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
    private void Preview_Click(object sender,RoutedEventArgs e)
    {
        FlushFilters();
        if(Vm?.Page is not {} page || Vm.SelectedTable is not {} table || !Vm.CanPrint)return;
        try { new SalesReportPrintWindow(SalesReportPaper.Build(StoreIdentity.Current.Name,page,table)) { Owner=Window.GetWindow(this) }.ShowDialog(); }
        catch(Exception ex) { MessageBox.Show("تعذرت معاينة التقرير: "+ex.Message); }
    }
    private async void Export_Click(object sender,RoutedEventArgs e)
    {
        FlushFilters();
        if(Vm?.Page is not {} page || !Vm.CanExport)return;
        var dialog=new SaveFileDialog { Filter="Excel Workbook (*.xlsx)|*.xlsx",FileName=$"Sales-{(Vm.IsMovement?"Activity":"Invoices")}-{Vm.From:yyyyMMdd}-{Vm.Through:yyyyMMdd}.xlsx",DefaultExt=".xlsx",AddExtension=true };
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var store=StoreIdentity.Current.Name; var path=dialog.FileName;
        try { await Task.Run(()=>SalesReportWorkbook.Save(path,store,page)); MessageBox.Show("تم حفظ الملخص وكل جداول العرض المفلتر في ملف Excel."); }
        catch(Exception ex) { MessageBox.Show("تعذّر حفظ ملف Excel: "+ex.Message); }
    }
}
