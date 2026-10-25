using System;
using GlassDesk.Platform.Displays;
using GlassDesk.Platform.Interop;

namespace GlassDesk.Platform;

public static class WindowShadowStyles
{
	private const int WsExTransparent = 32;

	private const int WsExToolWindow = 128;

	private const int WsExAppWindow = 262144;

	private const int WsExLayered = 524288;

	private const int WsExTopmost = 8;

	private const int RequiredStyles = 134742176;

	private const uint NoActivate = 16u;

	private const uint NoOwnerZOrder = 512u;

	private const uint NoMoveOrSize = 3u;

	public static bool MakeInputTransparent(nint window)
	{
		if (window == 0)
		{
			return false;
		}
		int windowLong = User32.GetWindowLong(window, -20);
		User32.SetWindowLong(window, -20, (windowLong | 0x80800A0) & -262145);
		return IsInputTransparent(window);
	}

	public static bool IsInputTransparent(nint window)
	{
		if (window == 0)
		{
			return false;
		}
		int windowLong = User32.GetWindowLong(window, -20);
		if ((windowLong & 0x80800A0) == 134742176)
		{
			return (windowLong & 0x40000) == 0;
		}
		return false;
	}

	public static bool IsOwnerVisible(nint owner)
	{
		if (owner != 0 && User32.IsWindowVisible(owner))
		{
			return !User32.IsIconic(owner);
		}
		return false;
	}

	public static bool PositionBehind(nint shadow, nint owner, PixelRect bounds)
	{
		if (shadow == 0 || owner == 0 || shadow == owner)
		{
			return false;
		}
		if (!DisplayService.MoveWindowToPixelRect(shadow, bounds))
		{
			return false;
		}
		bool flag = (User32.GetWindowLong(owner, -20) & 8) != 0;
		bool flag2 = (User32.GetWindowLong(shadow, -20) & 8) != 0;
		if (flag != flag2 && !Dpi32.SetWindowPos(shadow, flag ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 531u))
		{
			return false;
		}
		return Dpi32.SetWindowPos(shadow, owner, 0, 0, 0, 0, 531u);
	}
}
