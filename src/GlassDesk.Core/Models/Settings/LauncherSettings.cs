namespace GlassDesk.Core.Models.Settings;

/// <summary>启动台的网格、窗口与搜索行为。</summary>
public sealed class LauncherSettings
{
    /// <summary>每页的网格行数。</summary>
    public int Rows { get; set; } = 5;

    /// <summary>每页的网格列数。</summary>
    public int Columns { get; set; } = 7;

    /// <summary>
    /// 窗口模式下占工作区的比例。计划书要求大约
    /// 三分之二，居中。
    /// </summary>
    public double WindowWorkAreaRatio { get; set; } = 0.66;

    /// <summary>铺满整个工作区，而不是显示居中的面板。</summary>
    public bool FullscreenMode { get; set; }

    /// <summary>
    /// 启用后，启动台打开时将光标放进搜索框。默认关闭。
    /// </summary>
    public bool FocusSearchOnOpen { get; set; }

    /// <summary>
    /// 再次打开时恢复上次停留的页面，关闭则总是从第一页开始。
    /// </summary>
    public bool RememberLastPage { get; set; } = true;

    /// <summary>启动台图标尺寸（28 至 80 DIP，默认 48）。</summary>
    public double IconSize { get; set; } = 48.0;

    /// <summary>启动台背景透明度百分比（10 至 100，默认 70%）。</summary>
    public double GlassOpacity { get; set; } = 70.0;

    /// <summary>材质风格：true 为纯净通透毛玻璃（GlassBoard 简约风），false 为原版液态流光水波。</summary>
    public bool PureFrostedGlass { get; set; }

    /// <summary>是否在网格上方显示横向分类胶囊标签栏。</summary>
    public bool ShowCategoryTabs { get; set; } = true;

    /// <summary>是否显示次级操作工具栏（图标滑块、快捷添加等）。</summary>
    public bool ShowSubToolbar { get; set; } = true;

    /// <summary>用户自定义的工作区/分类列表（如 办公、开发、音乐 等）。</summary>
    public List<string> CustomCategories { get; set; } = [];

    /// <summary>用户已删除或隐藏的分类名称（包括系统预设的 系统工具、音乐 等）。</summary>
    public List<string> HiddenCategories { get; set; } = [];
}
