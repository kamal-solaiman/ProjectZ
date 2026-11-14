using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProjectZ.Models;

namespace ProjectZ.Views;

public partial class ColorPickerWindow : Window
{
    private bool ready;
    public string SelectedHex { get; private set; } = "#147D92";

    public ColorPickerWindow(string initialHex)
    {
        InitializeComponent();
        SetHex(ThemeColorMath.IsHex(initialHex) ? initialHex : "#147D92");
        ready = true;
    }

    private void SetHex(string hex)
    {
        ready = false;
        try
        {
            SelectedHex = hex.ToUpperInvariant();
            var (r, g, b) = ThemeColorMath.Parse(SelectedHex);
            HexBox.Text = SelectedHex;
            RedSlider.Value = r; GreenSlider.Value = g; BlueSlider.Value = b;
            RedValue.Text = r.ToString(); GreenValue.Text = g.ToString(); BlueValue.Text = b.ToString();
            Preview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            SampleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeColorMath.ReadableInk(SelectedHex)));
            HexError.Visibility = Visibility.Collapsed;
            OkButton.IsEnabled = true;
        }
        finally { ready = true; }
    }

    private void Channel_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        SetHex(ThemeColorMath.Hex((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value));
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        if (ThemeColorMath.IsHex(HexBox.Text)) SetHex(HexBox.Text);
        else
        {
            HexError.Visibility = Visibility.Visible;
            OkButton.IsEnabled = false;
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) SetHex(hex);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemeColorMath.IsHex(HexBox.Text)) return;
        SelectedHex = HexBox.Text.ToUpperInvariant();
        DialogResult = true;
    }
}
