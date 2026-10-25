using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace GlassDesk.Platform;

public static class SystemRadioController
{
	public static Task<SystemRadioState> ReadWifiAsync()
	{
		return ReadAsync(RadioKind.WiFi);
	}

	public static Task<SystemRadioState> ReadBluetoothAsync()
	{
		return ReadAsync(RadioKind.Bluetooth);
	}

	public static Task<bool> SetWifiAsync(bool enabled)
	{
		return SetAsync(RadioKind.WiFi, enabled);
	}

	public static Task<bool> SetBluetoothAsync(bool enabled)
	{
		return SetAsync(RadioKind.Bluetooth, enabled);
	}

	private static async Task<SystemRadioState> ReadAsync(RadioKind kind)
	{
		try
		{
			if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
			{
				return new SystemRadioState(Available: false, IsOn: false, CanControl: false);
			}
			Radio radio = (await Radio.GetRadiosAsync()).FirstOrDefault((Radio candidate) => candidate.Kind == kind);
			return ((object)radio == null) ? new SystemRadioState(Available: false, IsOn: false, CanControl: false) : new SystemRadioState(Available: true, radio.State == RadioState.On, CanControl: true);
		}
		catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is PlatformNotSupportedException || ex is TypeLoadException || ex is COMException) ? true : false)
		{
			return new SystemRadioState(Available: false, IsOn: false, CanControl: false);
		}
	}

	private static async Task<bool> SetAsync(RadioKind kind, bool enabled)
	{
		try
		{
			if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
			{
				return false;
			}
			Radio radio = (await Radio.GetRadiosAsync()).FirstOrDefault((Radio candidate) => candidate.Kind == kind);
			if ((object)radio == null)
			{
				return false;
			}
			return await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
		}
		catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is PlatformNotSupportedException || ex is TypeLoadException || ex is COMException) ? true : false)
		{
			return false;
		}
	}
}
