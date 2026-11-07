using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FitohPhone.Helpers;

// Shows an element only when the bound bool is true; collapses it otherwise so
// the layout does not reserve space for an empty error line.
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
