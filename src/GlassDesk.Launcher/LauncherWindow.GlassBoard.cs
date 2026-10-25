using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GlassDesk.AppServices;
using GlassDesk.Core.Models;
using GlassDesk.Core.Storage;
using GlassDesk.UI.Theming;
using Microsoft.Win32;

namespace GlassDesk.Launcher;

public partial class LauncherWindow
{
    internal bool _isMultiSelectMode;
    internal readonly HashSet<string> _selectedItemIds = new(StringComparer.Ordinal);
    public Action? ShowSettingsAction { get; set; }

    private void InitializeGlassBoardControls()
    {
        if (BoardIconSlider != null)
        {
            BoardIconSlider.Value = Math.Clamp(_settings.IconSize, 28, 80);
            IconSizeText.Text = $"图标 {Math.Round(BoardIconSlider.Value)} px";
        }

        if (TransparencySlider != null)
        {
            TransparencySlider.Value = Math.Clamp(_settings.GlassOpacity, 10, 100);
            TransparencyText.Text = $"透明度 {Math.Round(TransparencySlider.Value)}%";
            ApplyBackdropOpacity(TransparencySlider.Value);
        }

        if (SearchHint != null && SearchBox != null)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        SyncGroupSelectorCombo();
    }

    private void ApplyBackdropOpacity(double transparencyPercent)
    {
        if (FrostedBackdrop != null)
        {
            var alpha = Math.Clamp(1.0 - (transparencyPercent / 100.0), 0.08, 0.96);
            FrostedBackdrop.Opacity = alpha;
        }

        if (ThemeService.Current?.Appearance is { } app)
        {
            app.LauncherGlassTransparency = transparencyPercent / 100.0;
        }
        GlassSurface?.RequestBackdropRefresh();
    }

    private void SyncGroupSelectorCombo()
    {
        if (GroupSelectorCombo == null || _services == null) return;

        var categories = new List<string> { "全部", "未分组" };
        var custom = _services.Library.Items
            .Where(i => i != null)
            .Select(i => !string.IsNullOrWhiteSpace(i!.Category) ? i.Category! : AppClassifier.Classify(i.DisplayName, i.Target))
            .OfType<string>()
            .Where(c => !string.IsNullOrWhiteSpace(c) && c != "全部" && c != "未分组")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
        categories.AddRange(custom);

        GroupSelectorCombo.ItemsSource = categories;
        GroupSelectorCombo.SelectedItem = string.IsNullOrEmpty(_selectedCategory) ? "全部" : _selectedCategory;

        if (BulkMoveTargetCombo != null)
        {
            BulkMoveTargetCombo.ItemsSource = categories.Where(c => c != "全部").ToList();
            if (BulkMoveTargetCombo.Items.Count > 0) BulkMoveTargetCombo.SelectedIndex = 0;
        }
    }

    private void OnBatchAddClick(object sender, RoutedEventArgs e)
    {
        OnAddClick(sender, e);
    }

    private void OnMultiSelectToggleClick(object sender, RoutedEventArgs e)
    {
        _isMultiSelectMode = !_isMultiSelectMode;
        if (_isMultiSelectMode)
        {
            MultiSelectButton.Content = "完成";
            BulkBar.Visibility = Visibility.Visible;
            SyncGroupSelectorCombo();
            UpdateBulkBar();
        }
        else
        {
            MultiSelectButton.Content = "多选";
            BulkBar.Visibility = Visibility.Collapsed;
            _selectedItemIds.Clear();
        }
        RenderTiles();
    }

    internal void UpdateBulkBar()
    {
        if (BulkSelectionCountText != null)
        {
            BulkSelectionCountText.Text = $"已选 {_selectedItemIds.Count} 项";
        }
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsAction?.Invoke();
    }

    private void OnMoreMenuButtonClick(object sender, RoutedEventArgs e)
    {
        var menu = CreateTrackedContextMenu();
        menu.PlacementTarget = sender as Button;

        AddMenu(menu, "按名称字母排序", () =>
        {
            if (_workspace is null) return;
            CommitEdit(layout =>
            {
                var allSlots = layout.Pages.SelectMany(p => p.Slots).ToList();
                var sorted = allSlots.OrderBy(s =>
                {
                    if (s.Kind == LauncherSlotKind.Group) return s.Group?.Name ?? "";
                    return _services?.Library.Find(s.ItemId)?.DisplayName ?? "";
                }).ToList();

                var result = new LauncherLayout();
                for (int i = 0; i < sorted.Count; i += _view.Capacity)
                {
                    var page = new LauncherPage { Id = $"page-{(i / _view.Capacity) + 1}", Slots = sorted.Skip(i).Take(_view.Capacity).ToList() };
                    result.Pages.Add(page);
                }
                return result;
            });
            ShowNotice("已按名称重新排序。");
        });

        AddMenu(menu, "切换分类栏显示", () =>
        {
            _settings.ShowCategoryTabs = !_settings.ShowCategoryTabs;
            CategoryScrollViewer.Visibility = _settings.ShowCategoryTabs ? Visibility.Visible : Visibility.Collapsed;
        });

        AddMenu(menu, "切换次级工具栏显示", () =>
        {
            _settings.ShowSubToolbar = !_settings.ShowSubToolbar;
            SubToolbar.Visibility = _settings.ShowSubToolbar ? Visibility.Visible : Visibility.Collapsed;
        });

        menu.IsOpen = true;
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        CloseWithTransition();
    }

    private void OnBoardIconSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IconSizeText != null)
        {
            IconSizeText.Text = $"图标 {Math.Round(e.NewValue)} px";
        }

        if (!IsLoaded || _workspace == null) return;
        _settings.IconSize = e.NewValue;

        var availableWidth = Math.Max(300, LauncherPanelRoot.Width - 90);
        var availableHeight = Math.Max(240, LauncherPanelRoot.Height - 230);
        var tileWidth = e.NewValue + 44;
        var tileHeight = e.NewValue + 48;

        var cols = Math.Clamp((int)Math.Floor(availableWidth / tileWidth), 3, 10);
        var rows = Math.Clamp((int)Math.Floor(availableHeight / tileHeight), 2, 7);

        _view.SetGrid(rows, cols);
        RenderTiles();
    }

    private void OnAddExistingIconClick(object sender, RoutedEventArgs e)
    {
        if (_services == null) return;
        var apps = _services.Library.Items.Where(i => i != null).ToList();
        var menu = CreateTrackedContextMenu();
        menu.PlacementTarget = sender as Button;

        var targetCat = string.IsNullOrEmpty(_selectedCategory) ? "全部" : _selectedCategory;
        foreach (var app in apps.Take(25))
        {
            var id = app.Id;
            var name = app.DisplayName;
            AddMenu(menu, $"{name} ({app.Category ?? "未分类"})", () =>
            {
                if (targetCat != "全部" && targetCat != "未分组")
                {
                    app.Category = targetCat;
                    _services.Library.Update(app);
                    ShowNotice($"已将「{name}」归入【{targetCat}】。");
                    RenderTiles();
                }
            });
        }
        menu.IsOpen = true;
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var values = AskFields("新建文件夹", ("文件夹名称", "新建文件夹"));
        if (values != null && !string.IsNullOrWhiteSpace(values[0]))
        {
            CreateGridFolder(values[0].Trim());
        }
    }

    public void CreateGridFolder(string folderName)
    {
        if (_workspace is null) return;
        CommitEdit(layout =>
        {
            var group = new LauncherGroup { Id = Guid.NewGuid().ToString("N"), Name = folderName, ItemIds = [] };
            var allSlots = layout.Pages.SelectMany(p => p.Slots).ToList();
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
        ShowNotice($"文件夹「{folderName}」已添加至启动台。");
    }

    public void PromptCreateCategory()
    {
        var values = AskFields("新建工作区", ("工作区 / 分类名称", "新工作区"));
        if (values == null || string.IsNullOrWhiteSpace(values[0])) return;
        var name = values[0].Trim();
        if (name == "全部" || name == "未分类")
        {
            ShowNotice("该名称为系统保留字，请换一个名称。");
            return;
        }

        if (_settings != null)
        {
            _settings.HiddenCategories.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (!_settings.CustomCategories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.CustomCategories.Add(name);
            }
            SaveLauncherSettings();
        }

        _selectedCategory = name;
        _view.MoveToPage(0);
        UpdateCategoryTabs();
        RenderTiles();
        ShowNotice($"工作区「{name}」已创建。可通过右键应用【归入工作区】将软件归类至此。");
    }

    public void DeleteCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == "全部") return;

        if (MessageBox.Show(this, $"确定要删除工作区/分类【{category}】吗？\n（不会删除软件本身，仅移除分类标签）", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_settings != null)
        {
            _settings.CustomCategories.RemoveAll(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
            if (!_settings.HiddenCategories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.HiddenCategories.Add(category);
            }
            SaveLauncherSettings();
        }

        if (_services != null)
        {
            var matched = _services.Library.Items.Where(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matched.Count > 0)
            {
                foreach (var app in matched)
                {
                    app.Category = null;
                    _services.Library.Update(app);
                }
                try { _services.Library.Save(); } catch { }
            }
        }

        if (string.Equals(_selectedCategory, category, StringComparison.OrdinalIgnoreCase))
        {
            _selectedCategory = null;
            _view.MoveToPage(0);
        }

        UpdateCategoryTabs();
        RenderTiles();
        ShowNotice($"工作区/分类「{category}」已删除。");
    }

    public void RenameCategory(string oldName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || oldName == "全部") return;
        var values = AskFields("重命名工作区", ("工作区名称", oldName));
        if (values == null || string.IsNullOrWhiteSpace(values[0])) return;
        var newName = values[0].Trim();
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return;

        if (_settings != null)
        {
            _settings.CustomCategories.RemoveAll(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase));
            _settings.HiddenCategories.RemoveAll(c => string.Equals(c, newName, StringComparison.OrdinalIgnoreCase));
            if (!_settings.CustomCategories.Any(c => string.Equals(c, newName, StringComparison.OrdinalIgnoreCase)))
                _settings.CustomCategories.Add(newName);
            SaveLauncherSettings();
        }

        if (_services != null)
        {
            var matched = _services.Library.Items.Where(a => string.Equals(a.Category, oldName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var app in matched)
            {
                app.Category = newName;
                _services.Library.Update(app);
            }
            if (matched.Count > 0)
            {
                try { _services.Library.Save(); } catch { }
            }
        }

        if (string.Equals(_selectedCategory, oldName, StringComparison.OrdinalIgnoreCase))
            _selectedCategory = newName;

        UpdateCategoryTabs();
        RenderTiles();
        ShowNotice($"工作区已重命名为「{newName}」。");
    }

    public void LaunchCategoryApps(string category)
    {
        if (_services is null) return;
        var apps = _services.Library.Items.Where(a => a is not null && a.IsUsable && string.Equals(ResolveCategory(a), category, StringComparison.OrdinalIgnoreCase)).ToList();
        if (apps.Count == 0)
        {
            ShowNotice($"工作区「{category}」内暂无应用。");
            return;
        }
        foreach (var app in apps)
        {
            _services.Launch.ActivateOrLaunch(app.Id);
        }
        ShowNotice($"已一键并发启动「{category}」全部 {apps.Count} 个应用。");
    }

    private void SaveLauncherSettings()
    {
        if (_settings == null || _services == null) return;
        try
        {
            _services.Settings.Current.Launcher = _settings;
            _services.Settings.Save();
        }
        catch (Exception ex)
        {
            ShowNotice("保存设置失败：" + ex.Message);
        }
    }

    public void AddAppsToCategory(string category)
    {
        if (_services is null) return;
        var apps = _services.Library.Items.Where(item => item is not null).ToList();
        var dialog = new WorkspaceEditorWindow(apps, _services.Icons)
        {
            Owner = this,
            Title = $"添加应用至【{category}】"
        };
        if (ShowModalDialog(dialog.ShowDialog))
        {
            var selectedIds = dialog.SelectedAppIds.ToHashSet(StringComparer.Ordinal);
            foreach (var app in _services.Library.Items)
            {
                if (selectedIds.Contains(app.Id))
                {
                    app.Category = category;
                    _services.Library.Update(app);
                }
            }
            try { _services.Library.Save(); } catch { }
            _selectedCategory = category;
            UpdateCategoryTabs();
            RenderTiles();
            ShowNotice($"已将 {selectedIds.Count} 个应用归入「{category}」。");
        }
    }

    public void MoveAppToGroup(string appId, string groupId)
    {
        CommitEdit(layout =>
        {
            var group = layout.Pages.SelectMany(p => p.Slots).FirstOrDefault(s => s.Group?.Id == groupId)?.Group;
            if (group == null) return layout;
            var newLayout = LauncherLayoutEditor.RemoveShortcut(layout, appId, _view.Capacity);
            var targetGroup = newLayout.Pages.SelectMany(p => p.Slots).FirstOrDefault(s => s.Group?.Id == groupId)?.Group;
            if (targetGroup != null && !targetGroup.ItemIds.Contains(appId))
            {
                targetGroup.ItemIds.Add(appId);
            }
            return newLayout;
        });
        ShowNotice("应用已移入文件夹。");
    }

    public void AddAppsToGroup(string groupId)
    {
        if (_services is null || _workspace is null) return;
        var group = _workspace.Layout.Pages.SelectMany(p => p.Slots).FirstOrDefault(s => s.Group?.Id == groupId)?.Group;
        if (group == null) return;

        var apps = _services.Library.Items.Where(item => item is not null).ToList();
        var dialog = new WorkspaceEditorWindow(apps, _services.Icons)
        {
            Owner = this,
            Title = $"添加应用至文件夹【{group.Name}】"
        };
        if (ShowModalDialog(dialog.ShowDialog))
        {
            var selectedIds = dialog.SelectedAppIds;
            CommitEdit(layout =>
            {
                var target = layout.Pages.SelectMany(p => p.Slots).FirstOrDefault(s => s.Group?.Id == groupId)?.Group;
                if (target == null) return layout;
                var currentLayout = layout;
                foreach (var id in selectedIds)
                {
                    if (!target.ItemIds.Contains(id))
                    {
                        currentLayout = LauncherLayoutEditor.RemoveShortcut(currentLayout, id, _view.Capacity);
                        var g = currentLayout.Pages.SelectMany(p => p.Slots).FirstOrDefault(s => s.Group?.Id == groupId)?.Group;
                        g?.ItemIds.Add(id);
                    }
                }
                return currentLayout;
            });
            ShowNotice($"已将选中的应用添加至「{group.Name}」。");
        }
    }

    private void OnNewItemMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = CreateTrackedContextMenu();
        menu.PlacementTarget = sender as Button;

        AddMenu(menu, "新建工作区 / 分类标签…", PromptCreateCategory);
        AddMenu(menu, "新建网格文件夹…", () =>
        {
            var values = AskFields("新建文件夹", ("文件夹名称", "新建文件夹"));
            if (values != null && !string.IsNullOrWhiteSpace(values[0]))
            {
                CreateGridFolder(values[0].Trim());
            }
        });
        AddMenu(menu, "从电脑添加文件夹快捷方式…", () =>
        {
            var picker = new OpenFolderDialog { Title = "选择要添加到启动台的文件夹" };
            if (ShowModalDialog(() => picker.ShowDialog(this)))
            {
                ImportPaths([picker.FolderName]);
            }
        });

        menu.IsOpen = true;
    }

    private void OnUndoActionClick(object sender, RoutedEventArgs e)
    {
        ShowNotice("已恢复至上次保存的启动台布局。");
        RefreshLibrary();
    }

    private void OnBulkSelectAllClick(object sender, RoutedEventArgs e)
    {
        var slots = CurrentSlots();
        foreach (var slot in slots)
        {
            if (slot.Kind == LauncherSlotKind.App && slot.ItemId != null)
            {
                _selectedItemIds.Add(slot.ItemId);
            }
        }
        UpdateBulkBar();
        RenderTiles();
    }

    private void OnBulkClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _selectedItemIds.Clear();
        UpdateBulkBar();
        RenderTiles();
    }

    private void OnBulkDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItemIds.Count == 0 || _services == null) return;

        if (MessageBox.Show(this, $"确定要从启动台中移除选中的 {_selectedItemIds.Count} 项应用吗？", "批量移除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var removeList = _selectedItemIds.ToList();
        CommitEdit(layout =>
        {
            var currentLayout = layout;
            foreach (var id in removeList)
            {
                currentLayout = LauncherLayoutEditor.RemoveShortcut(currentLayout, id, _view.Capacity);
            }
            return currentLayout;
        });

        _selectedItemIds.Clear();
        UpdateBulkBar();
        ShowNotice($"已批量移除 {removeList.Count} 项。");
        RenderTiles();
    }

    private void OnBulkMoveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItemIds.Count == 0 || _services == null) return;
        var targetCategory = BulkMoveTargetCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(targetCategory) || targetCategory == "全部") return;

        foreach (var id in _selectedItemIds)
        {
            var entry = _services.Library.Find(id);
            if (entry != null)
            {
                entry.Category = targetCategory == "未分组" ? null : targetCategory;
                _services.Library.Update(entry);
            }
        }

        ShowNotice($"已将 {_selectedItemIds.Count} 项应用移动至【{targetCategory}】。");
        _selectedItemIds.Clear();
        UpdateBulkBar();
        RenderTiles();
    }

    private void OnTransparencySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TransparencyText != null)
        {
            TransparencyText.Text = $"透明度 {Math.Round(e.NewValue)}%";
        }

        _settings.GlassOpacity = e.NewValue;
        ApplyBackdropOpacity(e.NewValue);
    }

    private void OnGroupSelectorComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupSelectorCombo.SelectedItem is string cat)
        {
            _selectedCategory = cat == "全部" ? null : cat;
            _view.MoveToPage(0);
            RenderTiles();
        }
    }
}
