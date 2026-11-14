using System.IO;
using System.Windows;
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
        // The smoke host is a separate EXE; pack URIs in the referenced WPF app must still resolve
        // against ProjectZ.dll, exactly as they do when ProjectZ.exe is the application assembly.
        Application.ResourceAssembly = typeof(ProjectZ.App).Assembly;
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
        Console.WriteLine("All WPF appearance smoke tests passed.");
    }
}
