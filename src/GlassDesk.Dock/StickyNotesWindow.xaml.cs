using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GlassDesk.Core.Storage;
using GlassDesk.Platform;
using GlassDesk.UI.Controls;
using Microsoft.Win32;

namespace GlassDesk.Dock;

public partial class StickyNotesWindow : GlassWindow
{
    private readonly StickyNotesStore _store;
    private bool _isPinnedToDesktop = true;

    public StickyNotesWindow()
    {
        InitializeComponent();

        var path = AppPaths.ForCurrentUser().NotesFile;
        _store = new StickyNotesStore(path);
        try
        {
            _store.Load();
        }
        catch
        {
            // 首次使用若不存在则忽略
        }

        Loaded += OnWindowLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyPinState();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(20, workArea.Right - Width - 30);
        Top = Math.Max(20, workArea.Top + 70);

        RenderNotes();
        NoteInput.Focus();
    }

    private void ApplyPinState()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;

        if (_isPinnedToDesktop)
        {
            Topmost = false;
            WindowStyles.SendToDesktopBottom(handle);
            PinGlyph.Text = ""; // Pin icon
            PinButton.ToolTip = "当前状态：已固定在桌面壁纸底层（点击切换为浮动置顶）";
        }
        else
        {
            Topmost = true;
            WindowStyles.BringToTopWithoutActivation(handle);
            PinGlyph.Text = ""; // Unpin icon
            PinButton.ToolTip = "当前状态：浮动置顶（点击切换为桌面底层）";
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _isPinnedToDesktop = !_isPinnedToDesktop;
        ApplyPinState();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void RenderNotes()
    {
        NotesContainer.Children.Clear();
        var items = _store.Items;

        int doneCount = items.Count(i => i.IsDone);
        SummaryText.Text = $"共 {items.Count} 项，已完成 {doneCount} 项";

        if (items.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "暂无待办事项，在下方输入框添加吧~",
                FontSize = 11.5,
                Opacity = 0.6,
                Margin = new Thickness(12, 24, 12, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            NotesContainer.Children.Add(emptyText);
            return;
        }

        foreach (var item in items)
        {
            var row = new DockPanel
            {
                Margin = new Thickness(4, 3, 4, 3),
                LastChildFill = true
            };

            var deleteBtn = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 },
                Width = 22,
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                Opacity = 0.5,
                ToolTip = "删除此项"
            };
            deleteBtn.MouseEnter += (_, _) => deleteBtn.Opacity = 1.0;
            deleteBtn.MouseLeave += (_, _) => deleteBtn.Opacity = 0.5;
            deleteBtn.Click += (_, _) =>
            {
                _store.Remove(item.Id);
                RenderNotes();
            };
            DockPanel.SetDock(deleteBtn, System.Windows.Controls.Dock.Right);
            row.Children.Add(deleteBtn);

            var check = new CheckBox
            {
                IsChecked = item.IsDone,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0)
            };
            check.Click += (_, _) =>
            {
                _store.ToggleDone(item.Id);
                RenderNotes();
            };
            DockPanel.SetDock(check, System.Windows.Controls.Dock.Left);
            row.Children.Add(check);

            var text = new TextBlock
            {
                Text = item.Content,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Opacity = item.IsDone ? 0.45 : 0.95
            };
            if (item.IsDone)
            {
                text.TextDecorations = TextDecorations.Strikethrough;
            }
            row.Children.Add(text);

            NotesContainer.Children.Add(row);
        }
    }

    private void OnAddNoteClick(object sender, RoutedEventArgs e)
    {
        SubmitNote();
    }

    private void OnNoteInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitNote();
            e.Handled = true;
        }
    }

    private void SubmitNote()
    {
        var text = NoteInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _store.Add(text);
        NoteInput.Clear();
        RenderNotes();
    }

    private void OnClearCompletedClick(object sender, RoutedEventArgs e)
    {
        _store.ClearCompleted();
        RenderNotes();
    }

    private void OnExportTextClick(object sender, RoutedEventArgs e)
    {
        var items = _store.Items;
        if (items.Count == 0)
        {
            MessageBox.Show(this, "当前便签中没有内容可导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出便签与待办清单",
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"AuraDesk_待办清单_{DateTime.Now:yyyyMMdd}.txt"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                using var writer = new StreamWriter(dialog.FileName, append: false, System.Text.Encoding.UTF8);
                writer.WriteLine($"# AuraDesk 便签与待办事项清单（导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}）");
                writer.WriteLine();
                foreach (var item in items)
                {
                    var status = item.IsDone ? "[x]" : "[ ]";
                    writer.WriteLine($"{status} {item.Content}");
                }
                MessageBox.Show(this, "便签清单已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
