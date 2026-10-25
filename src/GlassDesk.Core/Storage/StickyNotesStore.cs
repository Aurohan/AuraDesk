using GlassDesk.Core.Models;
using GlassDesk.Core.Serialization;

namespace GlassDesk.Core.Storage;

/// <summary>
/// 负责 notes.json 的本地持久化与内存缓存。
/// </summary>
public sealed class StickyNotesStore
{
    private readonly JsonFileStore<StickyNotesData> _store;

    public StickyNotesStore(string path, TimeProvider? timeProvider = null)
    {
        _store = new JsonFileStore<StickyNotesData>(
            path,
            GlassDeskJsonContext.Default.StickyNotesData,
            timeProvider);
    }

    public string FilePath => _store.Path;

    public StickyNotesData Data { get; private set; } = new();

    public LoadOutcome<StickyNotesData> Load()
    {
        var outcome = _store.Load(static () => new StickyNotesData());
        Data = outcome.Value;
        Data.Items ??= [];
        return outcome;
    }

    public void Save()
    {
        Data.Items ??= [];
        _store.Save(Data);
    }

    public IReadOnlyList<StickyNoteItem> Items => Data.Items;

    public void Add(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        Data.Items.Add(new StickyNoteItem
        {
            Content = content.Trim(),
            IsDone = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        Save();
    }

    public void Remove(string id)
    {
        Data.Items.RemoveAll(item => item.Id == id);
        Save();
    }

    public void ToggleDone(string id)
    {
        var item = Data.Items.FirstOrDefault(i => i.Id == id);
        if (item is not null)
        {
            item.IsDone = !item.IsDone;
            Save();
        }
    }

    public void ClearCompleted()
    {
        Data.Items.RemoveAll(item => item.IsDone);
        Save();
    }
}
