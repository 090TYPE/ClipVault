using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;

namespace ClipVault.App.ViewModels;

public class HistoryViewModel
{
    private readonly IClipStore _store;
    private readonly IClipboardWriter _writer;

    public ObservableCollection<ClipItem> Items { get; } = new();

    public HistoryViewModel(IClipStore store, IClipboardWriter writer)
    {
        _store = store;
        _writer = writer;
    }

    public async Task LoadAsync(string? query = null)
    {
        var data = string.IsNullOrWhiteSpace(query)
            ? await _store.GetRecentAsync()
            : await _store.SearchAsync(query);
        Items.Clear();
        foreach (var i in data) Items.Add(i);
    }

    public void Paste(ClipItem item) => _writer.Write(item);

    public async Task TogglePinAsync(ClipItem item)
    {
        await _store.SetPinnedAsync(item.Id, !item.IsPinned);
        await LoadAsync();
    }

    public async Task DeleteAsync(ClipItem item)
    {
        await _store.DeleteAsync(item.Id);
        await LoadAsync();
    }
}
