using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void CategoryHeader_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && sender is Button { Tag: NavigationMenuCategoryViewModel category })
        {
            viewModel.ToggleCategory(category);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && sender is Button { Tag: NavigationMenuItemViewModel item })
        {
            viewModel.SelectItem(item);
        }
    }
}
