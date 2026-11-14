using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AppearanceView : UserControl
{
    public AppearanceView() => InitializeComponent();

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ColorEditorViewModel editor }) return;
        var picker = new ColorPickerWindow(editor.Hex) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true)
            editor.Hex = picker.SelectedHex;
    }
}
