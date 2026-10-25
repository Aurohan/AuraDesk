using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace GlassDesk.Platform;

public static class AudioOutputController
{
	private struct PropertyKey(Guid format, uint id)
	{
		public Guid Format = format;

		public uint Id = id;
	}

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	private struct PropertyValue
	{
		[FieldOffset(0)]
		public ushort Type;

		[FieldOffset(8)]
		public nint Pointer;
	}

	[ComImport]
	[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
	private sealed class PolicyConfig
	{
	}

	[ComImport]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(int flow, uint state, out IDeviceCollection devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
	}

	[ComImport]
	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDeviceCollection
	{
		[PreserveSig]
		int GetCount(out uint count);

		[PreserveSig]
		int Item(uint index, out IDevice device);
	}

	[ComImport]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDevice
	{
		[PreserveSig]
		int Activate(ref Guid id, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

		[PreserveSig]
		int OpenPropertyStore(uint access, out IPropertyStore properties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

		[PreserveSig]
		int GetState(out uint state);
	}

	[ComImport]
	[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyStore
	{
		[PreserveSig]
		int GetCount(out uint count);

		[PreserveSig]
		int GetAt(uint index, out PropertyKey key);

		[PreserveSig]
		int GetValue(ref PropertyKey key, out PropertyValue value);
	}

	[ComImport]
	[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPolicyConfig
	{
		[PreserveSig]
		int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, nint format);

		[PreserveSig]
		int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, nint format);

		[PreserveSig]
		int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);

		[PreserveSig]
		int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, nint endpoint, nint mix);

		[PreserveSig]
		int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, nint period, nint minimum);

		[PreserveSig]
		int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, nint period);

		[PreserveSig]
		int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);

		[PreserveSig]
		int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);

		[PreserveSig]
		int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fx, nint key, nint value);

		[PreserveSig]
		int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fx, nint key, nint value);

		[PreserveSig]
		int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);

		[PreserveSig]
		int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
	}

	public static string LastDiagnostic { get; private set; } = "尚未读取输出设备";

	public static IReadOnlyList<AudioOutputDevice> GetDevices()
	{
		object obj = null;
		IDeviceCollection devices = null;
		IDevice device = null;
		List<AudioOutputDevice> list = new List<AudioOutputDevice>();
		try
		{
			obj = new AudioDeviceEnumerator();
			IDeviceEnumerator deviceEnumerator = (IDeviceEnumerator)obj;
			string id = null;
			if (deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out device) >= 0)
			{
				device.GetId(out id);
			}
			Marshal.ThrowExceptionForHR(deviceEnumerator.EnumAudioEndpoints(0, 1u, out devices));
			Marshal.ThrowExceptionForHR(devices.GetCount(out var count));
			for (uint num = 0u; num < count; num++)
			{
				IDevice device2 = null;
				IPropertyStore properties = null;
				try
				{
					if (devices.Item(num, out device2) < 0 || device2.GetId(out string id2) < 0)
					{
						continue;
					}
					string text = "音频输出设备";
					if (device2.OpenPropertyStore(0u, out properties) >= 0)
					{
						PropertyKey key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14u);
						if (properties.GetValue(ref key, out var value) >= 0)
						{
							try
							{
								if (value.Type == 31)
								{
									text = Marshal.PtrToStringUni(value.Pointer) ?? text;
								}
							}
							finally
							{
								PropVariantClear(ref value);
							}
						}
					}
					list.Add(new AudioOutputDevice(id2, text, string.Equals(id2, id, StringComparison.OrdinalIgnoreCase)));
				}
				finally
				{
					Release(properties);
					Release(device2);
				}
			}
		}
		catch (Exception ex) when ((ex is COMException || ex is InvalidCastException || ex is PlatformNotSupportedException) ? true : false)
		{
			LastDiagnostic = ex.ToString();
			return list;
		}
		finally
		{
			Release(device);
			Release(devices);
			Release(obj);
		}
		LastDiagnostic = $"已读取 {list.Count} 个声音输出设备";
		return (from audioOutputDevice in list.DistinctBy((AudioOutputDevice audioOutputDevice) => audioOutputDevice.Id)
			orderby audioOutputDevice.IsDefault descending
			select audioOutputDevice).ThenBy((AudioOutputDevice audioOutputDevice) => audioOutputDevice.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	public static bool SetDefault(string id)
	{
		if (string.IsNullOrWhiteSpace(id) || !GetDevices().Any((AudioOutputDevice device) => device.Id == id))
		{
			return false;
		}
		object obj = null;
		try
		{
			obj = new PolicyConfig();
			IPolicyConfig policyConfig = (IPolicyConfig)obj;
			if (policyConfig.SetDefaultEndpoint(id, 0) < 0 || policyConfig.SetDefaultEndpoint(id, 1) < 0)
			{
				return false;
			}
			return GetDevices().Any((AudioOutputDevice device) => device.Id == id && device.IsDefault);
		}
		catch (Exception ex) when ((ex is COMException || ex is InvalidCastException || ex is PlatformNotSupportedException) ? true : false)
		{
			return false;
		}
		finally
		{
			Release(obj);
		}
	}

	private static void Release(object? value)
	{
		if (value != null && Marshal.IsComObject(value))
		{
			Marshal.ReleaseComObject(value);
		}
	}

	[DllImport("ole32.dll")]
	private static extern int PropVariantClear(ref PropertyValue value);
}
