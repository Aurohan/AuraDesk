using GlassDesk.AppServices;
using GlassDesk.Core.Contracts;
using GlassDesk.Core.Models;
using GlassDesk.Core.Storage;
using Xunit;

namespace GlassDesk.AppServices.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _tempFile;

    public WorkspaceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "apps_workspace_test_" + Guid.NewGuid().ToString("N") + ".json");
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
    public void WorkspaceAppEntry_ValidationAndLaunchRecognized()
    {
        var store = new AppLibraryStore(_tempFile);
        store.Load();

        var entry1 = new AppEntry
        {
            Id = "app1",
            DisplayName = "VS Code",
            Kind = AppItemKind.Program,
            Target = @"C:\Tools\Code.exe"
        };
        var entry2 = new AppEntry
        {
            Id = "app2",
            DisplayName = "Terminal",
            Kind = AppItemKind.Program,
            Target = @"C:\Tools\wt.exe"
        };

        var ws = new AppEntry
        {
            Id = "ws1",
            DisplayName = "开发工作区",
            Kind = AppItemKind.Workspace,
            Category = "工作区",
            Target = "workspace://dev",
            WorkspaceAppIds = [entry1.Id, entry2.Id]
        };

        Assert.True(ws.IsUsable);
        Assert.True(ws.HasLaunchTarget);

        store.Add(entry1);
        store.Add(entry2);
        store.Add(ws);

        var found = store.Find("ws1");
        Assert.NotNull(found);
        Assert.Equal(AppItemKind.Workspace, found.Kind);
        Assert.Equal(2, found.WorkspaceAppIds.Count);

        var launcher = new LaunchService(store);
        var res = launcher.ActivateOrLaunch("ws1");
        Assert.Equal(ActivationOutcome.Launched, res.Outcome);
    }
}
