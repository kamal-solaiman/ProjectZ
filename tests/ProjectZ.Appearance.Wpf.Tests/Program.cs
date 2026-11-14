using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new ProjectZ.App();
        app.InitializeComponent();
        var runtime = ThemeManager.Instance;
        runtime.Initialize();

        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
        }

        Check(Application.Current.Resources["Theme.Brush.Surface"] is Brush, "Application resource graph loads before any window");
        var studio = new AppearanceView { DataContext = new AppearanceViewModel(runtime) };
        var login = new LoginWindow();
        var main = new ProjectZ.Views.MainWindow();
        var picker = new ColorPickerWindow("#147D92");
        foreach (var theme in ThemeCatalog.All)
        {
            runtime.Preview(new AppearanceSettings { ThemeId = theme.Id, CustomPalette = ThemeCatalog.CustomPalette, CornerRadius = theme.CornerRadius, SurfaceOpacity = theme.SurfaceOpacity });
            foreach (var key in ThemeManager.ColorKeys)
            {
                if (Application.Current.Resources["Theme.Color." + key] is not Color)
                    throw new Exception("Missing live color: " + key);
                if (Application.Current.Resources["Theme.Brush." + key] is not Brush)
                    throw new Exception("Missing live brush: " + key);
            }
            studio.Measure(new Size(980, 760));
            studio.Arrange(new Rect(0, 0, 980, 760));
            studio.UpdateLayout();
            login.Measure(new Size(960, 640));
            login.Arrange(new Rect(0, 0, 960, 640));
            main.Measure(new Size(1320, 760));
            main.Arrange(new Rect(0, 0, 1320, 760));
            picker.Measure(new Size(520, 590));
            picker.Arrange(new Rect(0, 0, 520, 590));
            Check(true, "WPF layout and live resources apply: " + theme.Id);
        }
        // Regression: navigating to المظهر must notify IsAppearanceSelected so the host Grid
        // leaves Collapsed. A missing notification rendered the content area empty/white.
        var mainVm = new MainViewModel(new UserSession(0, "smoke", "اختبار الدخان"), () => { },
            appearance: new AppearanceViewModel(runtime));
        main.DataContext = mainVm;
        PumpDispatcher(); // let DataContext inheritance and style-trigger bindings attach
        var notified = new List<string?>();
        mainVm.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        mainVm.SelectItem(mainVm.Categories[6].Items[1]);
        PumpDispatcher(); // let the DataTrigger binding consume the PropertyChanged event
        Check(mainVm.IsAppearanceSelected, "SelectItem activates the appearance screen");
        Check(notified.Contains(nameof(MainViewModel.IsAppearanceSelected)),
            "SelectItem notifies IsAppearanceSelected so the visibility trigger re-evaluates");
        // An unshown Window never applies its template, so walk the logical tree (fully built
        // from XAML) instead of the visual tree, then lay out the host Grid directly.
        var hostedStudio = FindLogicalDescendant<AppearanceView>(main);
        Check(hostedStudio is not null, "MainWindow hosts AppearanceView in its logical tree");
        Check(hostedStudio!.Parent is Grid, "AppearanceView sits inside its visibility-trigger host Grid");
        var hostGrid = (Grid)hostedStudio.Parent;
        Check(ReferenceEquals(hostGrid.DataContext, mainVm), "Window DataContext inherits down to the appearance host Grid");
        Check(hostGrid.Visibility == Visibility.Visible,
            "Appearance host Grid is Visible after navigating to المظهر (DataTrigger re-evaluated)");
        hostGrid.Measure(new Size(1100, 700));
        hostGrid.Arrange(new Rect(0, 0, 1100, 700));
        hostGrid.UpdateLayout();
        Check(hostedStudio.ActualWidth > 100 && hostedStudio.ActualHeight > 100,
            "AppearanceView is laid out with a real size, not an empty white panel");
        ProjectZ.Tests.TextColorTests.Run(Check);
        Console.WriteLine("All WPF appearance smoke tests passed.");
    }

    // The test never calls Application.Run, so queued binding/trigger work would otherwise sit
    // in the dispatcher forever. Drain it down to Background priority like a mini message loop.
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindLogicalDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match) return match;
            if (child is DependencyObject node)
            {
                var result = FindLogicalDescendant<T>(node);
                if (result is not null) return result;
            }
        }
        return null;
    }
}
