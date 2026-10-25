namespace GlassDesk.Core.Models;

/// <summary>
/// 桌面便签与待办事项记录。
/// </summary>
public sealed class StickyNoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Content { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 持久化便签数据容器。
/// </summary>
public sealed class StickyNotesData
{
    public List<StickyNoteItem> Items { get; set; } = [];
}
