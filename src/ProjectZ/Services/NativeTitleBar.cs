using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ProjectZ.Models;

namespace ProjectZ.Services;

/// <summary>
/// Best-effort DWM title-bar tint. The caption buttons stay native and LTR; older Windows builds
/// simply ignore unsupported attributes instead of blocking the application.
/// </summary>
public static class NativeTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeLegacy = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void Apply(Window window, AppearanceSettings settings)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var palette = ThemeCatalog.PaletteFor(settings);
        var dark = ThemeColorMath.Luminance(palette.Surface) < .179 ? 1 : 0;
        if (!TrySet(hwnd, UseImmersiveDarkMode, dark))
            TrySet(hwnd, UseImmersiveDarkModeLegacy, dark);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TrySet(hwnd, CaptionColor, ColorRef(palette.Surface));
            TrySet(hwnd, TextColor, ColorRef(ThemeColorMath.EnsureTextContrast(palette.Text, palette.Surface)));
            TrySet(hwnd, BorderColor, ColorRef(palette.Border));
        }
    }

    private static int ColorRef(string hex)
    {
        var (r, g, b) = ThemeColorMath.Parse(hex);
        return r | (g << 8) | (b << 16);
    }

    private static bool TrySet(IntPtr hwnd, int attribute, int value)
        => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
