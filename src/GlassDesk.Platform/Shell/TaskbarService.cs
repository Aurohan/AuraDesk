using System.Runtime.InteropServices;
using GlassDesk.Platform.Displays;
using GlassDesk.Platform.Interop;

namespace GlassDesk.Platform.Shell;

/// <summary>
/// 系统任务栏状态探测与避让尺寸计算服务。
/// </summary>
public static class TaskbarService
{
    /// <summary>
    /// 查询 Windows 系统任务栏是否开启了「自动隐藏任务栏」模式。
    /// </summary>
    public static bool IsAutoHideEnabled()
    {
        try
        {
            var data = new Shell32.AppBarData
            {
                Size = (uint)Marshal.SizeOf<Shell32.AppBarData>(),
            };

            return (Shell32.SHAppBarMessage(Shell32.AbmGetState, ref data) & Shell32.AbsAutoHide) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取主显示器系统任务栏的物理像素厚度（高度）。
    /// 无论任务栏当前是否收起，均能准确推导其展开后的物理像素高度。
    /// </summary>
    public static int GetTaskbarThickness(DisplayInfo display)
    {
        ArgumentNullException.ThrowIfNull(display);

        // 1. 首选官方 API：SHAppBarMessage(ABM_GETTASKBARPOS)
        // 微软文档明确承诺：即使任务栏当前处于自动隐藏收起状态，该接口依然返回其完整展开区域矩形。
        try
        {
            var data = new Shell32.AppBarData
            {
                Size = (uint)Marshal.SizeOf<Shell32.AppBarData>(),
            };
            var result = Shell32.SHAppBarMessage(Shell32.AbmGetTaskbarPos, ref data);
            if (result != 0 && data.Rectangle.Height > 0)
            {
                int thickness = data.Edge is Shell32.AbeLeft or Shell32.AbeRight
                    ? data.Rectangle.Width
                    : data.Rectangle.Height;
                if (thickness > 0)
                {
                    return thickness;
                }
            }
        }
        catch
        {
            // 失败时平滑退回到备选方案
        }

        // 2. 次选：查询 Shell_TrayWnd 原生窗口矩形
        try
        {
            var tray = User32.FindWindow("Shell_TrayWnd", null);
            if (tray != 0 && DisplayService.GetWindowPixelBounds(tray) is { } bounds)
            {
                if (bounds.Height > 0)
                {
                    return bounds.Height;
                }
            }
        }
        catch
        {
            // 失败时平滑退回到保底方案
        }

        // 3. 保底：Windows 11 / 10 默认底部任务栏标准 48 DIP 按当前显示器 DPI 换算
        return Math.Max(1, display.ToPixels(48));
    }

    /// <summary>
    /// 根据系统任务栏状态与 Dock 设置，计算 Dock 停靠的有效工作区。
    /// </summary>
    /// <remarks>
    /// 当 Windows 系统个性化设置中开启「自动隐藏任务栏」时，Windows 会将 WorkArea
    /// 扩满整块屏幕（或仅保留 2 物理像素），导致原本停靠在任务栏上方的 Dock 沉入屏幕物理最底边。
    /// 开启本避让机制后，系统将自动检测任务栏自动隐藏状态，并在底部预留出任务栏展开高度的缓冲，
    /// 确保 Dock 保持在任务栏原本的上方，且当鼠标移至屏幕底边唤出任务栏时，两者绝不重叠。
    /// </remarks>
    public static PixelRect ResolveEffectiveWorkArea(
        DisplayInfo display,
        bool reserveWhenAutoHide,
        bool isBottomPlacement = true,
        bool? isAutoHideOverride = null,
        int? taskbarThicknessOverride = null)
    {
        ArgumentNullException.ThrowIfNull(display);

        var workArea = display.WorkArea;
        if (!reserveWhenAutoHide || !isBottomPlacement)
        {
            return workArea;
        }

        bool autoHide = isAutoHideOverride ?? IsAutoHideEnabled();
        if (autoHide)
        {
            int taskbarThickness = taskbarThicknessOverride ?? GetTaskbarThickness(display);
            int currentBottomGap = display.Bounds.Bottom - workArea.Bottom;

            // 当底边缝隙小于任务栏厚度时，说明 Windows 此时未将任务栏空间从 WorkArea 中扣除
            if (currentBottomGap < taskbarThickness)
            {
                int neededCut = taskbarThickness - currentBottomGap;
                int newHeight = Math.Max(1, workArea.Height - neededCut);
                return new PixelRect(workArea.X, workArea.Y, workArea.Width, newHeight);
            }
        }

        return workArea;
    }
}
