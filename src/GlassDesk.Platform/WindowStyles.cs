using System;
using GlassDesk.Platform.Interop;

namespace GlassDesk.Platform;

public static class WindowStyles
{
	private static readonly nint HwndTopmost = new IntPtr(-1);

	private const uint SwpNoSize = 1u;

	private const uint SwpNoMove = 2u;

	private const uint SwpNoActivate = 16u;

		public static bool SendToDesktopBottom(nint window)
	{
		if (window == 0) return false;
		try {
			return Dpi32.SetWindowPos(window, (nint)1, 0, 0, 0, 0, 19u);
		} catch {
			return false;
		}
	}

	public static bool BringToTopWithoutActivation(nint window)
	{
		if (window == 0)
		{
			return false;
		}
		try
		{
			return Dpi32.SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, 19u);
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return false;
		}
	}

	public static bool MakeNoActivate(nint window)
	{
		if (window == 0)
		{
			return false;
		}
		try
		{
			int windowLong = User32.GetWindowLong(window, -20);
			if ((windowLong & 0x8000000) != 0)
			{
				return true;
			}
			User32.SetWindowLong(window, -20, windowLong | 0x8000000);
			return IsNoActivate(window);
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return false;
		}
	}

	public static bool IsNoActivate(nint window)
	{
		if (window == 0)
		{
			return false;
		}
		try
		{
			return (User32.GetWindowLong(window, -20) & 0x8000000) != 0;
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return false;
		}
	}
}
