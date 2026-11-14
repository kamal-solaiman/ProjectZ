using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ProjectZ.Models;

namespace ProjectZ.Helpers;

/// <summary>Safe HEX-to-brush conversion for editor/swatches; partial typing never throws into the UI.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && ThemeColorMath.IsHex(hex))
        {
            var (r, g, b) = ThemeColorMath.Parse(hex);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
