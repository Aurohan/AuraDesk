using System.IO;
using System.IO.Compression;
using GlassDesk.Core.Models;
using GlassDesk.Core.Storage;
using Xunit;

namespace GlassDesk.Core.Tests;

public sealed class ZipBackupTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _paths;
    private readonly BackupService _backup;

    public ZipBackupTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GlassDesk_ZipTest_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_tempRoot);
        _paths.EnsureCreated();
        _backup = new BackupService(_paths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void BackupToZipAndRestoreFromZip_PreservesDataCorrectly()
    {
        // 1. Arrange: write some test data
        File.WriteAllText(_paths.SettingsFile, "{\n  \"schemaVersion\": 1\n}");
        File.WriteAllText(_paths.AppLibraryFile, "{\n  \"items\": []\n}");

        var zipPath = Path.Combine(_tempRoot, "test_backup.zip");

        // 2. Act: BackupToZip
        var backupRes = _backup.BackupToZip(zipPath);
        Assert.True(backupRes.Succeeded, backupRes.Error);
        Assert.True(File.Exists(zipPath));

        // Verify zip contains manifest.json and settings.json
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            Assert.Contains(archive.Entries, e => e.FullName == "manifest.json");
            Assert.Contains(archive.Entries, e => e.FullName == "settings.json");
        }

        // 3. Clear existing files
        File.Delete(_paths.SettingsFile);
        Assert.False(File.Exists(_paths.SettingsFile));

        // 4. Act: RestoreFromZip
        var restoreRes = _backup.RestoreFromZip(zipPath);
        Assert.True(restoreRes.Succeeded, restoreRes.Error);

        // 5. Assert: settings file restored
        Assert.True(File.Exists(_paths.SettingsFile));
    }
}
