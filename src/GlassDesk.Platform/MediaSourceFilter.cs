using System;

namespace GlassDesk.Platform;

internal static class MediaSourceFilter
{
	internal static bool IsExcluded(string? source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return false;
		}
		string text = source.Trim().Replace('/', '\\');
		text = text.Substring(text.LastIndexOf('\\') + 1).Split('!', 2)[0];
		if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			text = text2.Substring(0, text2.Length - 4);
		}
		if (!text.Equals("wallpaper32", StringComparison.OrdinalIgnoreCase) && !text.Equals("wallpaper64", StringComparison.OrdinalIgnoreCase) && !text.Equals("wallpaperservice32", StringComparison.OrdinalIgnoreCase) && !text.Equals("wallpaperservice64", StringComparison.OrdinalIgnoreCase) && !text.Equals("webwallpaper32", StringComparison.OrdinalIgnoreCase) && !text.Equals("webwallpaper64", StringComparison.OrdinalIgnoreCase) && !text.Equals("WallpaperEngine", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("Wallpaper Engine", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
