using System.Text.Json;
using ClipVault.Core.Abstractions;

namespace ClipVault.Sync;

/// This device's stable Id and display name, persisted to JSON.
public class DeviceIdentity
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Environment.MachineName;

    public static DeviceIdentity LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(path))
                       ?? Create(path);
        }
        catch { /* fall through */ }
        return Create(path);
    }

    private static DeviceIdentity Create(string path)
    {
        var id = new DeviceIdentity();
        File.WriteAllText(path, JsonSerializer.Serialize(id));
        return id;
    }

    public DeviceInfo ToInfo(int tcpPort) => new(DeviceId, Name, tcpPort);
}
