using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClipVault.App.ViewModels;
using ClipVault.Core.Models;

namespace ClipVault.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _vm;
    private readonly ListBox _list;
    private readonly TextBox _search;

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _list = this.FindControl<ListBox>("List")!;
        _search = this.FindControl<TextBox>("Search")!;
        _list.ItemsSource = _vm.Items;

        var status = this.FindControl<TextBlock>("StatusText");
        if (status is not null) status.Text = "◉ sync on";

        _search.KeyUp += async (_, _) => await _vm.LoadAsync(_search.Text);
        _list.DoubleTapped += (_, _) => PasteSelected();
        KeyDown += OnKeyDown;
        Opened += async (_, _) => { await _vm.LoadAsync(); _search.Focus(); };

        // Tray app: hide instead of destroying so the cached window can reopen.
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var sel = _list.SelectedItem as ClipItem;
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                break;
            case Key.Enter:
                PasteSelected();
                break;
            case Key.Delete when sel is not null:
                await _vm.DeleteAsync(sel);
                break;
            case Key.P when e.KeyModifiers == KeyModifiers.Control && sel is not null:
                await _vm.TogglePinAsync(sel);
                break;
        }
    }

    private void PasteSelected()
    {
        if (_list.SelectedItem is ClipItem item)
        {
            _vm.Paste(item);
            Hide();
        }
    }
}
