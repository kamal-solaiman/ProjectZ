using System.Windows;
using System.Windows.Controls;
using ProjectZ.Services;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // The view model subscribed to the static store-identity event; detach with the window so a
        // logged-out session is released.
        Closed += (_, _) => StoreIdentity.NotifyClosing();
    }

    private void CategoryHeader_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && sender is Button { Tag: NavigationMenuCategoryViewModel category })
        {
            if (viewModel.IsSidebarCollapsed)
                viewModel.OpenCategoryFromRail(category);
            else
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
