using System.IO;
using GlassDesk.Core.Storage;
using Xunit;

namespace GlassDesk.Core.Tests;

public sealed class StickyNotesStoreTests : IDisposable
{
    private readonly string _tempFile;

    public StickyNotesStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "notes_test_" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }
        catch { }
    }

    [Fact]
    public void AddToggleRemove_WorksAndPersistsToDisk()
    {
        var store = new StickyNotesStore(_tempFile);
        store.Load();

        Assert.Empty(store.Items);

        // Add
        store.Add("编写 GlassBoard 挂件功能");
        Assert.Single(store.Items);
        Assert.Equal("编写 GlassBoard 挂件功能", store.Items[0].Content);
        Assert.False(store.Items[0].IsDone);

        // Toggle
        var id = store.Items[0].Id;
        store.ToggleDone(id);
        Assert.True(store.Items[0].IsDone);

        // Reload to verify persistence
        var store2 = new StickyNotesStore(_tempFile);
        store2.Load();
        Assert.Single(store2.Items);
        Assert.True(store2.Items[0].IsDone);

        // Remove
        store2.Remove(id);
        Assert.Empty(store2.Items);
    }
}
