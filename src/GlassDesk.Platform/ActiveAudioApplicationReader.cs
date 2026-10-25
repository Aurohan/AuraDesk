using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace GlassDesk.Platform;

public static class ActiveAudioApplicationReader
{
	private enum EDataFlow
	{
		Render,
		Capture,
		All
	}

	private enum ERole
	{
		Console,
		Multimedia,
		Communications
	}

	private enum AudioSessionState
	{
		Inactive,
		Active,
		Expired
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out nint devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	private interface IMMDevice
	{
		[PreserveSig]
		int Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4")]
	private interface IAudioSessionManager2
	{
		[PreserveSig]
		int GetAudioSessionControl(ref Guid sessionGuid, uint streamFlags, out nint sessionControl);

		[PreserveSig]
		int GetSimpleAudioVolume(ref Guid sessionGuid, uint streamFlags, out nint simpleAudioVolume);

		[PreserveSig]
		int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);

		[PreserveSig]
		int RegisterSessionNotification(nint sessionNotification);

		[PreserveSig]
		int UnregisterSessionNotification(nint sessionNotification);

		[PreserveSig]
		int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint duckNotification);

		[PreserveSig]
		int UnregisterDuckNotification(nint duckNotification);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
	private interface IAudioSessionEnumerator
	{
		[PreserveSig]
		int GetCount(out int sessionCount);

		[PreserveSig]
		int GetSession(int sessionIndex, out IAudioSessionControl2 sessionControl);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
	private interface IAudioSessionControl2
	{
		[PreserveSig]
		int GetState(out AudioSessionState state);

		[PreserveSig]
		int GetDisplayName(out nint displayName);

		[PreserveSig]
		int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, nint eventContext);

		[PreserveSig]
		int GetIconPath(out nint iconPath);

		[PreserveSig]
		int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, nint eventContext);

		[PreserveSig]
		int GetGroupingParam(out Guid groupingId);

		[PreserveSig]
		int SetGroupingParam(ref Guid groupingId, nint eventContext);

		[PreserveSig]
		int RegisterAudioSessionNotification(nint events);

		[PreserveSig]
		int UnregisterAudioSessionNotification(nint events);

		[PreserveSig]
		int GetSessionIdentifier(out nint sessionIdentifier);

		[PreserveSig]
		int GetSessionInstanceIdentifier(out nint sessionInstanceIdentifier);

		[PreserveSig]
		int GetProcessId(out uint processId);

		[PreserveSig]
		int IsSystemSoundsSession();

		[PreserveSig]
		int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("87CE5498-68D6-44E5-AC9B-06A5F09F3C6D")]
	private interface ISimpleAudioVolume
	{
		[PreserveSig]
		int SetMasterVolume(float fLevel, ref Guid eventContext);

		[PreserveSig]
		int GetMasterVolume(out float pfLevel);

		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid eventContext);

		[PreserveSig]
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
	}

	private const uint ClsCtxAll = 23u;

	public static ActiveAudioApplicationSnapshot Read()
	{
		var all = ReadAll();
		return all.OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.Title)).FirstOrDefault();
	}

	public static List<ActiveAudioApplicationSnapshot> ReadAll()
	{
		object obj = null;
		IMMDevice device = null;
		object instance = null;
		IAudioSessionEnumerator sessionEnumerator = null;
		List<ActiveAudioApplicationSnapshot> list = new List<ActiveAudioApplicationSnapshot>();
		try
		{
			obj = new AudioDeviceEnumerator();
			if (((IMMDeviceEnumerator)obj).GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) != 0 || device == null)
			{
				return list;
			}
			Guid iid = typeof(IAudioSessionManager2).GUID;
			if (device.Activate(ref iid, 23u, 0, out instance) != 0 || !(instance is IAudioSessionManager2 audioSessionManager) || audioSessionManager.GetSessionEnumerator(out sessionEnumerator) != 0 || sessionEnumerator.GetCount(out var sessionCount) != 0)
			{
				return list;
			}
			for (int i = 0; i < sessionCount; i++)
			{
				IAudioSessionControl2 sessionControl = null;
				try
				{
					if (sessionEnumerator.GetSession(i, out sessionControl) != 0 || sessionControl == null || sessionControl.GetState(out var state) != 0 || state != AudioSessionState.Active || sessionControl.GetProcessId(out var processId) != 0 || processId == 0 || processId == Environment.ProcessId)
					{
						continue;
					}
					using Process process = Process.GetProcessById((int)processId);
					if (!process.HasExited && !MediaSourceFilter.IsExcluded(process.ProcessName))
					{
						string text = ResolveApplicationName(process);
						if (!MediaSourceFilter.IsExcluded(text))
						{
							string text2 = SafeMainWindowTitle(process);
							float vol = 1.0f;
							bool muted = false;
							if (sessionControl is ISimpleAudioVolume simpleVol)
							{
								simpleVol.GetMasterVolume(out vol);
								simpleVol.GetMute(out muted);
							}
							int volPercent = Math.Clamp((int)Math.Round(vol * 100f), 0, 100);
							list.Add(new ActiveAudioApplicationSnapshot(Available: true, string.IsNullOrWhiteSpace(text2) ? (text + " 正在播放") : text2, text, (int)processId, volPercent, muted));
						}
					}
				}
				catch (Exception ex) when ((ex is ArgumentException || ex is InvalidOperationException || ex is Win32Exception || ex is COMException) ? true : false)
				{
				}
				finally
				{
					ReleaseCom(sessionControl);
				}
			}
			return list;
		}
		catch (Exception ex2) when ((ex2 is COMException || ex2 is InvalidCastException || ex2 is DllNotFoundException || ex2 is EntryPointNotFoundException || ex2 is PlatformNotSupportedException) ? true : false)
		{
			return list;
		}
		finally
		{
			ReleaseCom(sessionEnumerator);
			ReleaseCom(instance);
			ReleaseCom(device);
			ReleaseCom(obj);
		}
	}

	public static bool SetApplicationVolume(int processId, int volumePercent)
	{
		return WithSession(processId, simpleVol =>
		{
			float level = (float)Math.Clamp(volumePercent, 0, 100) / 100f;
			Guid ctx = Guid.Empty;
			return simpleVol.SetMasterVolume(level, ref ctx) == 0;
		});
	}

	public static bool SetApplicationMute(int processId, bool isMuted)
	{
		return WithSession(processId, simpleVol =>
		{
			Guid ctx = Guid.Empty;
			return simpleVol.SetMute(isMuted, ref ctx) == 0;
		});
	}

	private static bool WithSession(int processId, Func<ISimpleAudioVolume, bool> action)
	{
		object obj = null;
		IMMDevice device = null;
		object instance = null;
		IAudioSessionEnumerator sessionEnumerator = null;
		try
		{
			obj = new AudioDeviceEnumerator();
			if (((IMMDeviceEnumerator)obj).GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) != 0 || device == null)
			{
				return false;
			}
			Guid iid = typeof(IAudioSessionManager2).GUID;
			if (device.Activate(ref iid, 23u, 0, out instance) != 0 || !(instance is IAudioSessionManager2 audioSessionManager) || audioSessionManager.GetSessionEnumerator(out sessionEnumerator) != 0 || sessionEnumerator.GetCount(out var sessionCount) != 0)
			{
				return false;
			}
			for (int i = 0; i < sessionCount; i++)
			{
				IAudioSessionControl2 sessionControl = null;
				try
				{
					if (sessionEnumerator.GetSession(i, out sessionControl) == 0 && sessionControl != null)
					{
						if (sessionControl.GetProcessId(out var pid) == 0 && pid == (uint)processId)
						{
							if (sessionControl is ISimpleAudioVolume simpleVol)
							{
								return action(simpleVol);
							}
						}
					}
				}
				finally
				{
					ReleaseCom(sessionControl);
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
		finally
		{
			ReleaseCom(sessionEnumerator);
			ReleaseCom(instance);
			ReleaseCom(device);
			ReleaseCom(obj);
		}
	}

	private static string ResolveApplicationName(Process process)
	{
		try
		{
			string text = process.MainModule?.FileVersionInfo.FileDescription;
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		catch (Exception ex) when ((ex is Win32Exception || ex is InvalidOperationException || ex is NotSupportedException) ? true : false)
		{
		}
		return process.ProcessName;
	}

	private static string SafeMainWindowTitle(Process process)
	{
		try
		{
			return process.MainWindowTitle;
		}
		catch (InvalidOperationException)
		{
			return string.Empty;
		}
	}

	private static void ReleaseCom(object? value)
	{
		if (value != null && Marshal.IsComObject(value))
		{
			Marshal.ReleaseComObject(value);
		}
	}
}
