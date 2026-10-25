using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassDesk.Core.Imaging;
using GlassDesk.Platform.Shell;
using GlassDesk.UI.Controls;

namespace GlassDesk.Launcher;

/// <summary>
/// 从 Windows「开始」菜单批量扫描并选择要导入到启动台的快捷方式。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF window resources are released in Closed, matching GlassWindow and DesktopImportWindow.")]
public sealed class StartMenuImportWindow : GlassWindow
{
    private sealed class ItemRow
    {
        public required StartMenuShortcut Item { get; init; }
        public required CheckBox Choice { get; init; }
        public required Image Icon { get; init; }
        public bool Visible { get; set; } = true;
    }

    private readonly List<ItemRow> _rows = [];
    private readonly ListBox _list;
    private readonly TextBox _searchBox;
    private readonly TextBlock _selectionCount = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _add = new() { Content = "添加所选", MinWidth = 100, IsDefault = true, IsEnabled = false };
    private LauncherIconLoader? _icons;
    private bool _changingSelection, _closed;

    public IReadOnlyList<string> SelectedPaths => _rows
        .Where(row => row.Choice.IsChecked == true)
        .Select(row => row.Item.Path)
        .ToArray();

    public StartMenuImportWindow(StartMenuScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        Title = "从「开始」菜单批量添加";
        AutomationProperties.SetName(this, Title);
        Width = 640;
        Height = 600;
        MinWidth = 460;
        MinHeight = 360;
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        NameScope.SetNameScope(this, new NameScope());

        var panel = new DockPanel { Margin = new Thickness(22) };

        // 头部标题与描述
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = "勾选要添加到启动台的应用程序或快捷方式。支持输入名称快速筛选。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            Opacity = 0.8
        });

        // 实时过滤搜索框
        _searchBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            ToolTip = "输入关键词筛选应用名称或分组"
        };
        AutomationProperties.SetName(_searchBox, "搜索快捷方式");
        _searchBox.TextChanged += (_, _) => FilterItems(_searchBox.Text);
        heading.Children.Add(_searchBox);

        // 全选 / 取消全选 / 选中计数
        var selection = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(_selectionCount, Dock.Right);
        selection.Children.Add(_selectionCount);

        var selectActions = new StackPanel { Orientation = Orientation.Horizontal };
        var selectAll = new Button { Content = "全选当前", MinWidth = 76, Margin = new Thickness(0, 0, 8, 0) };
        var clear = new Button { Content = "取消全选", MinWidth = 82 };
        RegisterName("SelectAllButton", selectAll);
        RegisterName("ClearSelectionButton", clear);
        selectAll.Click += (_, _) => SelectAllVisible(true);
        clear.Click += (_, _) => SelectAllVisible(false);
        selectActions.Children.Add(selectAll);
        selectActions.Children.Add(clear);
        selection.Children.Add(selectActions);
        heading.Children.Add(selection);

        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);

        // 底部确认 / 取消
        var footer = new StackPanel();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "取消", MinWidth = 76, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        RegisterName("AddSelectedButton", _add);
        _add.Click += (_, _) => { if (SelectedPaths.Count > 0) DialogResult = true; };
        actions.Children.Add(cancel);
        actions.Children.Add(_add);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);

        // 列表区
        _list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_list, "开始菜单项目");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        RegisterName("StartMenuItemsList", _list);

        foreach (var item in scan.Shortcuts)
        {
            var icon = new Image { Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0), Stretch = Stretch.Uniform };
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.Medium });
            var subText = (item.FromCurrentUser ? "个人菜单" : "公共菜单")
                + (string.IsNullOrWhiteSpace(item.Group) ? "" : $" · {item.Group}");
            label.Children.Add(new TextBlock
            {
                Text = subText,
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(0, 3, 0, 0)
            });

            var rowPanel = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            rowPanel.Children.Add(icon);
            rowPanel.Children.Add(label);

            var choice = new CheckBox
            {
                Content = rowPanel,
                ToolTip = item.Path,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 5, 4, 5)
            };
            AutomationProperties.SetName(choice, item.Name);
            choice.Checked += (_, _) => UpdateSelection();
            choice.Unchecked += (_, _) => UpdateSelection();

            var row = new ItemRow { Item = item, Choice = choice, Icon = icon };
            _rows.Add(row);
            _list.Items.Add(choice);
        }

        if (_rows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "「开始」菜单中未扫描到可添加的应用程序快捷方式。",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            });
        }
        else
        {
            panel.Children.Add(_list);
        }

        var surface = new Grid();
        surface.Children.Add(new LiquidGlassSurface { UseLauncherTransparency = true });
        surface.Children.Add(panel);
        Content = surface;

        UpdateSelection();
        Loaded += (_, _) =>
        {
            _searchBox.Focus();
            LoadIcons();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _icons?.Dispose();
        };
    }

    private void FilterItems(string? query)
    {
        var filter = (query ?? string.Empty).Trim();
        _changingSelection = true;
        try
        {
            _list.Items.Clear();
            foreach (var row in _rows)
            {
                var match = filter.Length == 0
                    || row.Item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || row.Item.Group.Contains(filter, StringComparison.OrdinalIgnoreCase);

                row.Visible = match;
                if (match)
                {
                    _list.Items.Add(row.Choice);
                }
            }
        }
        finally
        {
            _changingSelection = false;
            UpdateSelection();
        }
    }

    private void SelectAllVisible(bool selected)
    {
        _changingSelection = true;
        try
        {
            foreach (var row in _rows.Where(r => r.Visible))
            {
                row.Choice.IsChecked = selected;
            }
        }
        finally
        {
            _changingSelection = false;
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        if (_changingSelection) return;
        var selectedCount = _rows.Count(row => row.Choice.IsChecked == true);
        var visibleCount = _rows.Count(row => row.Visible);
        _selectionCount.Text = visibleCount == _rows.Count
            ? $"已选 {selectedCount} / {_rows.Count} 项"
            : $"已选 {selectedCount} 项（当前匹配 {visibleCount} / {_rows.Count}）";
        _add.IsEnabled = selectedCount > 0;
    }

    private void LoadIcons()
    {
        if (_icons is not null || _closed || _rows.Count == 0) return;
        _icons = new LauncherIconLoader();
        foreach (var row in _rows)
        {
            _ = LoadIconAsync(row.Item.Path, row.Icon);
        }
    }

    private async Task LoadIconAsync(string path, Image host)
    {
        var image = await _icons!.LoadAsync(path, () =>
        {
            var extracted = IconExtractor.Extract(path);
            if (extracted is null) return null;
            using var stream = new MemoryStream(PngWriter.EncodeBgra(extracted.Bgra, extracted.Width, extracted.Height));
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = 48;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }).ConfigureAwait(false);

        if (image is null || host.Dispatcher.HasShutdownStarted) return;
        try
        {
            await host.Dispatcher.InvokeAsync(() =>
            {
                if (!_closed) host.Source = image;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (TaskCanceledException) { }
    }
}
