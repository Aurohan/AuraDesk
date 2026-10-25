using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GlassDesk.AppServices;
using GlassDesk.Core.Contracts;
using GlassDesk.Core.Models;
using GlassDesk.Core.Storage;

namespace GlassDesk.Launcher;

public partial class LauncherWindow
{
    private DispatcherTimer? _noticeTimer;

    private void ActivateEntry(string id)
    {
        if (_services is null) return;
        var result = _services.Launch.ActivateOrLaunch(id);
        if (result.Outcome == ActivationOutcome.MultipleWindowsFound)
        {
            var menu = CreateTrackedContextMenu();
            foreach (var candidate in result.Candidates)
            {
                var item = new MenuItem { Header = candidate.Title };
                item.Click += (_, _) =>
                {
                    var activation = LaunchService.ActivateWindow(candidate.Handle);
                    if (activation.Outcome == ActivationOutcome.Failed) ShowNotice(activation.Message ?? "窗口未能激活。");
                    else CloseWithTransition();
                };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = TileGrid;
            menu.IsOpen = true;
        }
        else if (result.Outcome == ActivationOutcome.Failed)
            ShowNotice(result.Message ?? "应用未能打开。");
        else
            CloseWithTransition();
    }

    private void ShowNotice(string notice, bool autoDismiss = false)
    {
        _noticeTimer?.Stop();
        _noticeTimer = null;
        _notice = notice;
        RefreshDiagnostics();
        if (!autoDismiss) return;

        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_noticeTimer, timer)) return;
            _noticeTimer = null;
            // A library refresh may have replaced this success with a persistent status.
            if (_notice != notice) return;
            _notice = null;
            RefreshDiagnostics();
        };
        _noticeTimer = timer;
        timer.Start();
    }

    private void CommitEdit(Func<LauncherLayout, LauncherLayout> transform)
    {
        if (_workspace is null || _view.IsSearching)
        {
            ShowNotice("搜索结果中不能整理应用；请先清空搜索。");
            return;
        }
        var edit = _workspace.BeginEdit();
        try
        {
            edit.Draft = transform(edit.Draft);
            if (!_workspace.Commit(edit)) ShowNotice("应用库或布局已变化，请重试整理操作。");
            else _notice = null;
        }
        catch (Exception ex) when (ex is StorageException or InvalidOperationException or ArgumentException)
        {
            ShowNotice("整理未保存，原布局已恢复：" + ex.Message);
        }
        RenderTiles();
    }

    private ContextMenu BuildTileMenu(LauncherSlot slot)
    {
        var menu = CreateTrackedContextMenu();
        var id = slot.Kind == LauncherSlotKind.Group ? slot.Group?.Id : slot.ItemId;
        if (id is null) return menu;
        if (slot.Kind == LauncherSlotKind.Group)
        {
            AddMenu(menu, "打开文件夹", () => { _view.OpenGroupId = id; RenderGroup(); });
            AddMenu(menu, "重命名文件夹…", () => RenameGroup(id));
            AddMenu(menu, "添加应用到此文件夹…", () => AddAppsToGroup(id));
            AddMenu(menu, "🗑 解散 / 删除文件夹", () =>
            {
                CommitEdit(layout => LauncherLayoutEditor.Dissolve(layout, id, _view.Capacity));
                ShowNotice("文件夹已解散删除。");
            });
        }
        else
        {
            var entry = _services?.Library.Find(id);
            if (entry?.Kind == AppItemKind.Workspace)
            {
                AddMenu(menu, "一键启动工作区", () => ActivateEntry(id));
                AddMenu(menu, "编辑工作区…", () => EditWorkspace(id));
                AddMenu(menu, "从启动台移除", () => CommitEdit(layout => LauncherLayoutEditor.RemoveShortcut(layout, id, _view.Capacity)));
                AddMenu(menu, "从库中彻底删除", () =>
                {
                    _services?.Library.Remove(id);
                    _workspace?.Reconcile(_view.Capacity);
                    RefreshLibrary();
                    ShowNotice($"工作区「{entry.DisplayName}」已删除。");
                });
                return menu;
            }
            AddMenu(menu, "打开", () => ActivateEntry(id));
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Target) && entry.Kind != AppItemKind.Url)
            {
                AddMenu(menu, "打开文件所在位置", () => OpenFileLocation(entry.Target));
            }

            // 归入工作区/分类子菜单
            var catMenu = new MenuItem { Header = "归入工作区 / 分类" };
            var activeCats = GetActiveCategories();
            foreach (var cat in activeCats)
            {
                var targetCat = cat;
                var item = new MenuItem { Header = targetCat };
                item.Click += (_, _) =>
                {
                    if (entry != null)
                    {
                        entry.Category = targetCat;
                        _services?.Library.Update(entry);
                        try { _services?.Library.Save(); } catch { }
                        ShowNotice($"已将「{entry.DisplayName}」归入【{targetCat}】。");
                        UpdateCategoryTabs();
                        RenderTiles();
                    }
                };
                catMenu.Items.Add(item);
            }
            var newCatItem = new MenuItem { Header = "＋ 新建工作区并移入…" };
            newCatItem.Click += (_, _) =>
            {
                var values = AskFields("新建工作区", ("工作区名称", "新工作区"));
                if (values != null && !string.IsNullOrWhiteSpace(values[0]))
                {
                    var catName = values[0].Trim();
                    if (_settings != null)
                    {
                        _settings.HiddenCategories.Remove(catName);
                        if (!_settings.CustomCategories.Contains(catName))
                            _settings.CustomCategories.Add(catName);
                        SaveLauncherSettings();
                    }
                    if (entry != null)
                    {
                        entry.Category = catName;
                        _services?.Library.Update(entry);
                        try { _services?.Library.Save(); } catch { }
                    }
                    _selectedCategory = catName;
                    UpdateCategoryTabs();
                    RenderTiles();
                    ShowNotice($"已创建工作区「{catName}」并移入应用。");
                }
            };
            catMenu.Items.Add(new Separator());
            catMenu.Items.Add(newCatItem);
            menu.Items.Add(catMenu);

            // 移入文件夹子菜单
            var allGroups = (_dragEdit?.Draft ?? _workspace?.Layout)?.Pages.SelectMany(p => p.Slots)
                .Where(s => s.Kind == LauncherSlotKind.Group && s.Group != null)
                .Select(s => s.Group!)
                .ToList() ?? [];

            var folderSubmenu = new MenuItem { Header = "移入文件夹" };
            foreach (var g in allGroups)
            {
                var targetG = g;
                var item = new MenuItem { Header = targetG.Name };
                item.Click += (_, _) => MoveAppToGroup(id, targetG.Id);
                folderSubmenu.Items.Add(item);
            }
            var newFolderItem = new MenuItem { Header = "＋ 新建文件夹并移入…" };
            newFolderItem.Click += (_, _) =>
            {
                var values = AskFields("新建文件夹", ("文件夹名称", "新建文件夹"));
                if (values != null && !string.IsNullOrWhiteSpace(values[0]))
                {
                    var folderName = values[0].Trim();
                    var newGroupId = Guid.NewGuid().ToString("N");
                    CommitEdit(layout =>
                    {
                        var group = new LauncherGroup { Id = newGroupId, Name = folderName, ItemIds = [id] };
                        var newLayout = LauncherLayoutEditor.RemoveShortcut(layout, id, _view.Capacity);
                        var allSlots = newLayout.Pages.SelectMany(p => p.Slots).ToList();
                        var targetIndex = Math.Min(_view.Page * _view.Capacity, allSlots.Count);
                        allSlots.Insert(targetIndex, new LauncherSlot { Kind = LauncherSlotKind.Group, Group = group });
                        var result = new LauncherLayout { SchemaVersion = 2 };
                        for (int i = 0; i < allSlots.Count; i += _view.Capacity)
                        {
                            var page = new LauncherPage { Id = $"page-{(i / _view.Capacity) + 1}", Slots = allSlots.Skip(i).Take(_view.Capacity).ToList() };
                            result.Pages.Add(page);
                        }
                        return result;
                    });
                    ShowNotice($"已创建文件夹「{folderName}」并移入应用。");
                }
            };
            if (allGroups.Count > 0)
                folderSubmenu.Items.Add(new Separator());
            folderSubmenu.Items.Add(newFolderItem);
            menu.Items.Add(folderSubmenu);

            AddMenu(menu, "编辑名称、图标和别名", () => EditEntry(id));
            if (_services?.Library.Find(id)?.IsTargetMissing == true)
                AddMenu(menu, "重新定位…", () => RelocateEntry(id));
            var group = _workspace?.Layout.Pages.SelectMany(page => page.Slots)
                .SingleOrDefault(item => item.Group?.ItemIds.Contains(id) == true)?.Group;
            if (group is not null)
            {
                var index = group.ItemIds.IndexOf(id);
                if (index > 0)
                    AddMenu(menu, "在文件夹中前移", () => CommitEdit(layout => LauncherLayoutEditor.MoveGroupMember(layout, group.Id, id, index - 1)));
                if (index < group.ItemIds.Count - 1)
                    AddMenu(menu, "在文件夹中后移", () => CommitEdit(layout => LauncherLayoutEditor.MoveGroupMember(layout, group.Id, id, index + 1)));
                AddMenu(menu, "移出文件夹", () => CommitEdit(layout => LauncherLayoutEditor.RemoveFromGroup(layout, id, group.Id, _view.Page, 0, _view.Capacity)));
            }
            AddMenu(menu, "从启动台移除", () => CommitEdit(layout => LauncherLayoutEditor.RemoveShortcut(layout, id, _view.Capacity)));
        }
        return menu;
    }

    private static void AddMenu(ContextMenu menu, string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }


    private static void OpenFileLocation(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            string path = Environment.ExpandEnvironmentVariables(target);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
            }
        }
        catch
        {
        }
    }

}
