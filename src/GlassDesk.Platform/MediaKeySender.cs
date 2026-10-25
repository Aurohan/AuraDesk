using System;
using System.Runtime.InteropServices;

namespace GlassDesk.Platform;

public static class MediaKeySender
{
	public static bool Send(MediaKeyCommand command)
	{
		try
		{
			byte virtualKey = command switch
			{
				MediaKeyCommand.Previous => (byte)177, 
				MediaKeyCommand.Next => (byte)176, 
				_ => (byte)179, 
			};
			KeybdEvent(virtualKey, 0, 0u, 0u);
			KeybdEvent(virtualKey, 0, 2u, 0u);
			return true;
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return false;
		}
	}

	[DllImport("user32.dll", EntryPoint = "keybd_event")]
	private static extern void KeybdEvent(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}
