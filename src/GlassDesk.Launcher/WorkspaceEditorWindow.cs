using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassDesk.AppServices;
using GlassDesk.Core.Imaging;
using GlassDesk.Core.Models;
using GlassDesk.UI.Controls;

namespace GlassDesk.Launcher;

/// <summary>
/// 创建或编辑工作区（一键成组并发启动多个关联应用）。
/// </summary>
public sealed class WorkspaceEditorWindow : GlassWindow
{
    private readonly TextBox _nameBox = new() { Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _searchBox = new() { Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(6, 4, 6, 4) };
    private readonly ListBox _appList = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _selectionCount = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _confirmButton = new() { MinWidth = 100, IsDefault = true };
    private readonly List<AppRowItem> _rows = [];
    private readonly IconCache? _icons;

    public string WorkspaceName => _nameBox.Text.Trim();
    public bool IsDeleted { get; private set; }
    public IReadOnlyList<string> SelectedAppIds => _rows
        .Where(r => r.Choice.IsChecked == true)
        .Select(r => r.Entry.Id)
        .ToList();

    private sealed class AppRowItem
    {
        public required AppEntry Entry { get; init; }
        public required CheckBox Choice { get; init; }
        public required Image Icon { get; init; }
    }

    public WorkspaceEditorWindow(IReadOnlyList<AppEntry> availableApps, IconCache? icons, AppEntry? existing = null)
    {
        ArgumentNullException.ThrowIfNull(availableApps);
        _icons = icons;

        var isEditing = existing is not null;
        Title = isEditing ? "编辑工作区" : "新建工作区";
        AutomationProperties.SetName(this, Title);
        Width = 580;
        Height = 620;
        MinWidth = 440;
        MinHeight = 360;
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _nameBox.Text = existing?.DisplayName ?? "新工作区";
        _confirmButton.Content = isEditing ? "保存变更" : "创建工作区";

        var panel = new DockPanel { Margin = new Thickness(22) };

        // Header
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = "工作区允许将多个关联软件（如 IDE、终端、浏览器或通讯工具）绑定为一个磁贴，点击一键并发唤起。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
            Opacity = 0.8
        });

        // Name input
        heading.Children.Add(new TextBlock { Text = "工作区名称：", FontSize = 12, FontWeight = FontWeights.Medium });
        heading.Children.Add(_nameBox);

        // Filter / Search input
        heading.Children.Add(new TextBlock { Text = "从当前应用库中勾选关联应用：", FontSize = 12, FontWeight = FontWeights.Medium });
        _searchBox.TextChanged += (_, _) => FilterList();
        heading.Children.Add(_searchBox);

        // Selection actions
        var selectionBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_selectionCount, Dock.Right);
        selectionBar.Children.Add(_selectionCount);

        var selectActions = new StackPanel { Orientation = Orientation.Horizontal };
        var selectAll = new Button { Content = "全选", MinWidth = 66, Margin = new Thickness(0, 0, 8, 0) };
        var clear = new Button { Content = "取消全选", MinWidth = 82 };
        selectAll.Click += (_, _) => SetAllSelection(true);
        clear.Click += (_, _) => SetAllSelection(false);
        selectActions.Children.Add(selectAll);
        selectActions.Children.Add(clear);
        selectionBar.Children.Add(selectActions);
        heading.Children.Add(selectionBar);

        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);

        // Footer buttons
        var footer = new StackPanel();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "取消", MinWidth = 76, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        _confirmButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(WorkspaceName))
            {
                MessageBox.Show(this, "请输入有效的工作区名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                _nameBox.Focus();
                return;
            }
            if (SelectedAppIds.Count == 0)
            {
                MessageBox.Show(this, "请至少勾选一个关联应用。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        };

        if (isEditing)
        {
            var deleteButton = new Button
            {
                Content = "删除此工作区",
                Foreground = Brushes.Crimson,
                MinWidth = 96,
                Margin = new Thickness(0, 0, 16, 0)
            };
            deleteButton.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"确定要彻底删除工作区【{existing?.DisplayName ?? ""}】吗？\n（不会删除关联的原程序）", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    IsDeleted = true;
                    DialogResult = true;
                }
            };
            actions.Children.Add(deleteButton);
        }

        actions.Children.Add(cancel);
        actions.Children.Add(_confirmButton);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);

        // Build items
        var preselected = new HashSet<string>(existing?.WorkspaceAppIds ?? [], StringComparer.Ordinal);
        ScrollViewer.SetHorizontalScrollBarVisibility(_appList, ScrollBarVisibility.Disabled);

        foreach (var entry in availableApps.Where(a => a.Kind != AppItemKind.Workspace && a.IsUsable))
        {
            var icon = new Image { Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0), Stretch = Stretch.Uniform };
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = entry.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.Medium });
            var categoryText = string.IsNullOrWhiteSpace(entry.Category) ? "未分类" : entry.Category;
            label.Children.Add(new TextBlock { Text = $"{categoryText} · {entry.Target}", FontSize = 11, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });

            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(label);

            var choice = new CheckBox
            {
                Content = row,
                ToolTip = entry.Target,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 5, 4, 5),
                IsChecked = preselected.Contains(entry.Id),
            };

            choice.Checked += (_, _) => UpdateSelectionCount();
            choice.Unchecked += (_, _) => UpdateSelectionCount();

            var item = new AppRowItem { Entry = entry, Choice = choice, Icon = icon };
            _rows.Add(item);
        }

        FilterList();
        panel.Children.Add(_appList);

        var surface = new Grid();
        surface.Children.Add(new LiquidGlassSurface { UseLauncherTransparency = true });
        surface.Children.Add(panel);
        Content = surface;

        UpdateSelectionCount();
        Loaded += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
            LoadIcons();
        };
    }

    private void FilterList()
    {
        var filter = _searchBox.Text.Trim();
        _appList.Items.Clear();

        foreach (var row in _rows)
        {
            var match = string.IsNullOrEmpty(filter)
                || row.Entry.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (row.Entry.Category?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || row.Entry.Target.Contains(filter, StringComparison.OrdinalIgnoreCase);

            if (match)
            {
                _appList.Items.Add(row.Choice);
            }
        }
    }

    private void SetAllSelection(bool select)
    {
        foreach (var item in _appList.Items.OfType<CheckBox>())
        {
            item.IsChecked = select;
        }
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var count = SelectedAppIds.Count;
        _selectionCount.Text = $"已选 {count} / {_rows.Count} 个应用";
        _confirmButton.IsEnabled = count > 0;
    }

    private void LoadIcons()
    {
        if (_icons is null) return;
        foreach (var row in _rows)
        {
            try
            {
                var custom = row.Entry.CustomIconPath;
                var source = row.Entry.IconSource;
                var path = (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) ? custom : _icons.PathFor(source ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(path);
                    bi.DecodePixelWidth = 32;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    row.Icon.Source = bi;
                }
            }
            catch
            {
                // 忽略非关键单项图标解码失败
            }
        }
    }
}
