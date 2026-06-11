using System.Text.Json;
using ClipVault.Core.Abstractions;

namespace ClipVault.Sync;

public class TrustStore
{
    private readonly string _path;
    private readonly List<PairedDevice> _devices;

    public TrustStore(string path)
    {
        _path = path;
        _devices = Load();
    }

    public IReadOnlyList<PairedDevice> All => _devices;

    public void Add(PairedDevice device)
    {
        _devices.RemoveAll(d => d.DeviceId == device.DeviceId);
        _devices.Add(device);
        Save();
    }

    public PairedDevice? Get(string deviceId) =>
        _devices.FirstOrDefault(d => d.DeviceId == deviceId);

    private List<PairedDevice> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<PairedDevice>>(File.ReadAllText(_path))
                       ?? new List<PairedDevice>();
        }
        catch { /* corrupt → start empty */ }
        return new List<PairedDevice>();
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_devices));
}
