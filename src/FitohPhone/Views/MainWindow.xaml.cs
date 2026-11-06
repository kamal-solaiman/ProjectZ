using System.Windows;
using System.Windows.Controls;
using FitohPhone.Helpers;

namespace FitohPhone.Views;

public partial class MainWindow : Window
{
    private bool syncingTheme;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void QuickThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingTheme || !IsLoaded) return;
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string themeKey } })
        {
            ThemeManager.ApplyTheme(themeKey);
        }
    }

    private void OnThemeChanged(string themeKey)
    {
        if (Template is null) return;
        if (ShellNavTabs.Template?.FindName("QuickThemeSelector", ShellNavTabs) is ComboBox selector)
        {
            syncingTheme = true;
            foreach (var item in selector.Items)
            {
                if (item is ComboBoxItem { Tag: string tag } && tag == themeKey)
                {
                    selector.SelectedItem = item;
                    break;
                }
            }
            syncingTheme = false;
        }
    }
}
