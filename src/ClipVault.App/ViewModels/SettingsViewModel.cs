using System.Collections.Generic;
using System.Threading.Tasks;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;

namespace ClipVault.App.ViewModels;

public class SettingsViewModel
{
    private readonly ISyncService _sync;

    public AppSettings Settings { get; }

    public SettingsViewModel(AppSettings settings, ISyncService sync)
    {
        Settings = settings;
        _sync = sync;
    }

    public void Save() => Settings.Save();

    public IReadOnlyList<DiscoveredPeer> Devices => _sync.DiscoveredPeers;
    public string EnterPairingMode() => _sync.EnterPairingMode();
    public Task<bool> PairAsync(DiscoveredPeer peer, string pin) => _sync.PairWithAsync(peer, pin);
}
