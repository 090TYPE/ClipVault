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

        WireSync();
    }

    private void WireSync()
    {
        var devices = this.FindControl<ListBox>("Devices")!;
        var showPin = this.FindControl<Button>("ShowPinBtn")!;
        var pinLabel = this.FindControl<TextBlock>("PinLabel")!;
        var pinEntry = this.FindControl<TextBox>("PinEntry")!;
        var pairBtn = this.FindControl<Button>("PairBtn")!;
        var pairStatus = this.FindControl<TextBlock>("PairStatus")!;

        devices.ItemsSource = _vm.Devices;
        devices.DisplayMemberBinding =
            new Avalonia.Data.Binding(nameof(ClipVault.Core.Abstractions.DiscoveredPeer.Name));

        showPin.Click += (_, _) => pinLabel.Text = "PIN: " + _vm.EnterPairingMode();

        pairBtn.Click += async (_, _) =>
        {
            if (devices.SelectedItem is ClipVault.Core.Abstractions.DiscoveredPeer peer
                && !string.IsNullOrWhiteSpace(pinEntry.Text))
            {
                pairStatus.Text = "Pairing…";
                var ok = await _vm.PairAsync(peer, pinEntry.Text!);
                pairStatus.Text = ok ? "Paired ✓" : "Failed ✗";
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
