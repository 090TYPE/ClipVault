using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClipVault.App.ViewModels;

namespace ClipVault.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        var hotkey = this.FindControl<TextBox>("Hotkey")!;
        var maxUnpinned = this.FindControl<NumericUpDown>("MaxUnpinned")!;
        var maxAge = this.FindControl<NumericUpDown>("MaxAge")!;
        var save = this.FindControl<Button>("SaveBtn")!;

        hotkey.Text = _vm.Settings.Hotkey;
        maxUnpinned.Value = _vm.Settings.MaxUnpinned;
        maxAge.Value = _vm.Settings.MaxAgeDays;

        save.Click += (_, _) =>
        {
            _vm.Settings.Hotkey = string.IsNullOrWhiteSpace(hotkey.Text) ? "Ctrl+Shift+V" : hotkey.Text;
            _vm.Settings.MaxUnpinned = (int)(maxUnpinned.Value ?? 500);
            _vm.Settings.MaxAgeDays = (int)(maxAge.Value ?? 30);
            _vm.Save();
            Close();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
