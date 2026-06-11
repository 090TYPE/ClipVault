using ClipVault.Core.Models;

namespace ClipVault.App.ViewModels;

public class SettingsViewModel
{
    public AppSettings Settings { get; }
    public SettingsViewModel(AppSettings settings) => Settings = settings;
    public void Save() => Settings.Save();
}
