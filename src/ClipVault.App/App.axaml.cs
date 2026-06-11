using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClipVault.App.ViewModels;
using ClipVault.App.Views;

namespace ClipVault.App;

public partial class App : Application
{
    public AppServices Services { get; } = new();

    private HistoryWindow? _history;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app, no main window

        Services.Start(onHotkey: ShowHistory);
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowHistory()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _history ??= new HistoryWindow(new HistoryViewModel(Services.Store, Services.Writer));
            _history.Show();
            _history.Activate();
        });
    }

    private void OnOpenHistory(object? sender, System.EventArgs e) => ShowHistory();

    private void OnOpenSettings(object? sender, System.EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            new SettingsWindow(new SettingsViewModel(Services.Settings)).Show());
    }

    private void OnQuit(object? sender, System.EventArgs e) =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
