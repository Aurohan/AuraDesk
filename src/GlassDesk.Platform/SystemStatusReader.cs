using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace GlassDesk.Platform;

public static class SystemStatusReader
{
	private struct FileTime
	{
		public uint Low;

		public uint High;

		public readonly ulong ToUInt64()
		{
			return ((ulong)High << 32) | Low;
		}
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MemoryStatusEx
	{
		public uint Length;

		public uint MemoryLoad;

		public ulong TotalPhysical;

		public ulong AvailablePhysical;

		public ulong TotalPageFile;

		public ulong AvailablePageFile;

		public ulong TotalVirtual;

		public ulong AvailableVirtual;

		public ulong AvailableExtendedVirtual;
	}

	private struct SystemPowerStatus
	{
		public byte ACLineStatus;

		public byte BatteryFlag;

		public byte BatteryLifePercent;

		public byte SystemStatusFlag;

		public uint BatteryLifeTime;

		public uint BatteryFullLifeTime;
	}

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
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	private interface IAudioEndpointVolume
	{
		[PreserveSig]
		int RegisterControlChangeNotify(nint notify);

		[PreserveSig]
		int UnregisterControlChangeNotify(nint notify);

		[PreserveSig]
		int GetChannelCount(out uint channelCount);

		[PreserveSig]
		int SetMasterVolumeLevel(float levelDb, nint eventContext);

		[PreserveSig]
		int SetMasterVolumeLevelScalar(float level, nint eventContext);

		[PreserveSig]
		int GetMasterVolumeLevel(out float levelDb);

		[PreserveSig]
		int GetMasterVolumeLevelScalar(out float level);

		[PreserveSig]
		int SetChannelVolumeLevel(uint channel, float levelDb, nint eventContext);

		[PreserveSig]
		int SetChannelVolumeLevelScalar(uint channel, float level, nint eventContext);

		[PreserveSig]
		int GetChannelVolumeLevel(uint channel, out float levelDb);

		[PreserveSig]
		int GetChannelVolumeLevelScalar(uint channel, out float level);

		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, nint eventContext);

		[PreserveSig]
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}

	private static readonly object CpuGate = new object();

	private static ulong? _previousIdle;

	private static ulong? _previousKernel;

	private static ulong? _previousUser;

	private const uint ClsCtxAll = 23u;

	public static string LastAudioDiagnostic { get; private set; } = "尚未读取音量";

	private static long _lastInBytes;
	private static long _lastOutBytes;
	private static DateTime _lastSpeedTimestamp = DateTime.MinValue;
	private static readonly object NetworkGate = new object();

	public static (double DownBytesPerSec, double UpBytesPerSec, string DownText, string UpText) ReadNetworkSpeed()
	{
		lock (NetworkGate)
		{
			DateTime now = DateTime.UtcNow;
			long totalIn = 0;
			long totalOut = 0;
			try
			{
				NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface ni in interfaces)
				{
					if (ni.OperationalStatus == OperationalStatus.Up)
					{
						NetworkInterfaceType type = ni.NetworkInterfaceType;
						if (type != NetworkInterfaceType.Loopback && type != NetworkInterfaceType.Tunnel)
						{
							IPInterfaceStatistics stats = ni.GetIPStatistics();
							totalIn += stats.BytesReceived;
							totalOut += stats.BytesSent;
						}
					}
				}
			}
			catch
			{
				return (0, 0, "--", "--");
			}

			if (_lastSpeedTimestamp == DateTime.MinValue)
			{
				_lastInBytes = totalIn;
				_lastOutBytes = totalOut;
				_lastSpeedTimestamp = now;
				return (0, 0, "0 B/s", "0 B/s");
			}

			double elapsed = (now - _lastSpeedTimestamp).TotalSeconds;
			if (elapsed < 0.2)
			{
				return (0, 0, "0 B/s", "0 B/s");
			}

			long deltaIn = Math.Max(0, totalIn - _lastInBytes);
			long deltaOut = Math.Max(0, totalOut - _lastOutBytes);

			_lastInBytes = totalIn;
			_lastOutBytes = totalOut;
			_lastSpeedTimestamp = now;

			double downRate = deltaIn / elapsed;
			double upRate = deltaOut / elapsed;

			return (downRate, upRate, FormatSpeed(downRate), FormatSpeed(upRate));
		}
	}

	private static string FormatSpeed(double bytesPerSec)
	{
		if (bytesPerSec < 1024)
		{
			return $"{(int)bytesPerSec} B/s";
		}
		if (bytesPerSec < 1024 * 1024)
		{
			return $"{(bytesPerSec / 1024):F1} KB/s";
		}
		if (bytesPerSec < 1024 * 1024 * 1024)
		{
			return $"{(bytesPerSec / (1024 * 1024)):F1} MB/s";
		}
		return $"{(bytesPerSec / (1024 * 1024 * 1024)):F2} GB/s";
	}

	public static SystemStatusSnapshot Read()
	{
		(int?, bool, bool) tuple = ReadBattery();
		string networkLabel = ReadNetworkLabel();
		(int?, bool) tuple2 = ReadVolume();
		return new SystemStatusSnapshot(tuple.Item1, tuple.Item2, networkLabel, tuple2.Item1, tuple2.Item2)
		{
			HasBattery = tuple.Item3
		};
	}

	public static SystemPerformanceSnapshot ReadPerformance()
	{
		var netSpeed = ReadNetworkSpeed();
		return new SystemPerformanceSnapshot(
			ReadCpuPercent(), 
			ReadMemoryPercent(), 
			netSpeed.DownText, 
			netSpeed.UpText, 
			netSpeed.DownBytesPerSec, 
			netSpeed.UpBytesPerSec);
	}

	public static bool SetVolumePercent(int percent)
	{
		return WithAudioEndpoint((IAudioEndpointVolume endpoint) => endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(percent, 0, 100) / 100f, 0) == 0);
	}

	public static bool SetMuted(bool muted)
	{
		return WithAudioEndpoint((IAudioEndpointVolume endpoint) => endpoint.SetMute(muted, 0) == 0);
	}

	private static int? ReadCpuPercent()
	{
		try
		{
			if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
			{
				return null;
			}
			ulong num = idleTime.ToUInt64();
			ulong num2 = kernelTime.ToUInt64();
			ulong num3 = userTime.ToUInt64();
			lock (CpuGate)
			{
				ulong? previousIdle = _previousIdle;
				if (previousIdle.HasValue)
				{
					ulong valueOrDefault = previousIdle.GetValueOrDefault();
					previousIdle = _previousKernel;
					if (previousIdle.HasValue)
					{
						ulong valueOrDefault2 = previousIdle.GetValueOrDefault();
						previousIdle = _previousUser;
						if (previousIdle.HasValue)
						{
							ulong valueOrDefault3 = previousIdle.GetValueOrDefault();
							ulong val = num - valueOrDefault;
							ulong num4 = num2 - valueOrDefault2 + (num3 - valueOrDefault3);
							_previousIdle = num;
							_previousKernel = num2;
							_previousUser = num3;
							if (num4 == 0L)
							{
								return null;
							}
							return Math.Clamp((int)Math.Round((double)(num4 - Math.Min(val, num4)) * 100.0 / (double)num4), 0, 100);
						}
					}
				}
				_previousIdle = num;
				_previousKernel = num2;
				_previousUser = num3;
				return null;
			}
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return null;
		}
	}

	private static int? ReadMemoryPercent()
	{
		try
		{
			MemoryStatusEx status = new MemoryStatusEx
			{
				Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
			};
			return GlobalMemoryStatusEx(ref status) ? new int?(Math.Clamp((int)status.MemoryLoad, 0, 100)) : ((int?)null);
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return null;
		}
	}

	private static (int? Percent, bool Charging, bool HasBattery) ReadBattery()
	{
		try
		{
			if (!GetSystemPowerStatus(out var status))
			{
				return (Percent: null, Charging: false, HasBattery: true);
			}
			if (status.BatteryFlag != byte.MaxValue && (status.BatteryFlag & 0x80) != 0)
			{
				return (Percent: null, Charging: false, HasBattery: false);
			}
			return (Percent: (status.BatteryLifePercent == byte.MaxValue) ? ((int?)null) : new int?(Math.Clamp((int)status.BatteryLifePercent, 0, 100)), Charging: status.ACLineStatus == 1, HasBattery: true);
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException) ? true : false)
		{
			return (Percent: null, Charging: false, HasBattery: true);
		}
	}

	private static string ReadNetworkLabel()
	{
		try
		{
			NetworkInterface[] array = NetworkInterface.GetAllNetworkInterfaces().Where((NetworkInterface adapter) =>
			{
				bool flag = adapter.OperationalStatus == OperationalStatus.Up;
				if (flag)
				{
					NetworkInterfaceType networkInterfaceType = adapter.NetworkInterfaceType;
					bool flag2 = ((networkInterfaceType == NetworkInterfaceType.Loopback || networkInterfaceType == NetworkInterfaceType.Tunnel) ? true : false);
					flag = !flag2;
				}
				return flag;
			}).ToArray();
			if (array.Any((NetworkInterface adapter) => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
			{
				return "Wi-Fi";
			}
			return (array.Length != 0 && NetworkInterface.GetIsNetworkAvailable()) ? "联网" : "离线";
		}
		catch (NetworkInformationException)
		{
			return "未知";
		}
	}

	private static (int? Percent, bool Muted) ReadVolume()
	{
		object obj = null;
		IMMDevice device = null;
		object instance = null;
		try
		{
			obj = new AudioDeviceEnumerator();
			int defaultAudioEndpoint = ((IMMDeviceEnumerator)obj).GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
			if (defaultAudioEndpoint != 0 || device == null)
			{
				LastAudioDiagnostic = $"GetDefaultAudioEndpoint: 0x{defaultAudioEndpoint:X8}";
				return (Percent: null, Muted: false);
			}
			Guid iid = typeof(IAudioEndpointVolume).GUID;
			defaultAudioEndpoint = device.Activate(ref iid, 23u, 0, out instance);
			if (defaultAudioEndpoint != 0 || !(instance is IAudioEndpointVolume audioEndpointVolume))
			{
				LastAudioDiagnostic = $"Activate volume: 0x{defaultAudioEndpoint:X8}";
				return (Percent: null, Muted: false);
			}
			defaultAudioEndpoint = audioEndpointVolume.GetMasterVolumeLevelScalar(out var level);
			if (defaultAudioEndpoint != 0)
			{
				LastAudioDiagnostic = $"Read volume: 0x{defaultAudioEndpoint:X8}";
				return (Percent: null, Muted: false);
			}
			defaultAudioEndpoint = audioEndpointVolume.GetMute(out var mute);
			if (defaultAudioEndpoint != 0)
			{
				LastAudioDiagnostic = $"Read mute: 0x{defaultAudioEndpoint:X8}";
				return (Percent: null, Muted: false);
			}
			LastAudioDiagnostic = "音量读取成功";
			return (Percent: (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100f), Muted: mute);
		}
		catch (Exception ex) when ((ex is COMException || ex is InvalidCastException || ex is PlatformNotSupportedException) ? true : false)
		{
			LastAudioDiagnostic = ex.ToString();
			return (Percent: null, Muted: false);
		}
		finally
		{
			ReleaseCom(instance);
			ReleaseCom(device);
			ReleaseCom(obj);
		}
	}

	private static bool WithAudioEndpoint(Func<IAudioEndpointVolume, bool> action)
	{
		object obj = null;
		IMMDevice device = null;
		object instance = null;
		try
		{
			obj = new AudioDeviceEnumerator();
			if (((IMMDeviceEnumerator)obj).GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) != 0 || device == null)
			{
				return false;
			}
			Guid iid = typeof(IAudioEndpointVolume).GUID;
			if (device.Activate(ref iid, 23u, 0, out instance) != 0 || !(instance is IAudioEndpointVolume arg))
			{
				return false;
			}
			return action(arg);
		}
		catch (Exception ex) when ((ex is COMException || ex is InvalidCastException || ex is PlatformNotSupportedException) ? true : false)
		{
			return false;
		}
		finally
		{
			ReleaseCom(instance);
			ReleaseCom(device);
			ReleaseCom(obj);
		}
	}

	private static void ReleaseCom(object? value)
	{
		if (value != null && Marshal.IsComObject(value))
		{
			Marshal.ReleaseComObject(value);
		}
	}

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
