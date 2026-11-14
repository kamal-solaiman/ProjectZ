using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var notified = new List<string?>();
        mainVm.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        mainVm.SelectItem(mainVm.Categories[6].Items[1]);
        Check(mainVm.IsAppearanceSelected, "SelectItem activates the appearance screen");
        Check(notified.Contains(nameof(MainViewModel.IsAppearanceSelected)),
            "SelectItem notifies IsAppearanceSelected so the visibility trigger re-evaluates");
        main.ApplyTemplate(); // force the window template even though the window is never shown
        main.Measure(new Size(1320, 760));
        main.Arrange(new Rect(0, 0, 1320, 760));
        main.UpdateLayout();
        var hostedStudio = FindDescendant<AppearanceView>(main);
        Check(hostedStudio is not null, "MainWindow hosts AppearanceView in its visual tree");
        Check(hostedStudio!.Parent is Grid { Visibility: Visibility.Visible },
            "Appearance host Grid is Visible after navigating to المظهر");
        Check(hostedStudio.ActualWidth > 100 && hostedStudio.ActualHeight > 100,
            "AppearanceView is laid out with a real size, not an empty white panel");
        Console.WriteLine("All WPF appearance smoke tests passed.");
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        // VisualTreeHelper only accepts Visual/Visual3D; skip content elements (e.g. text inlines).
        if (root is not Visual) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}
