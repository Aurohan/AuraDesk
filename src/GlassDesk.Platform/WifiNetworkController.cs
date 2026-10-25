using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GlassDesk.Platform;

public static class WifiNetworkController
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WlanInterfaceInfo
	{
		public Guid InterfaceGuid;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string Description;

		public int State;
	}

	private struct Dot11Ssid
	{
		public uint Length;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		public byte[] Bytes;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WlanAvailableNetwork
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string ProfileName;

		public Dot11Ssid Ssid;

		public int BssType;

		public uint NumberOfBssids;

		[MarshalAs(UnmanagedType.Bool)]
		public bool NetworkConnectable;

		public uint NotConnectableReason;

		public uint NumberOfPhyTypes;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
		public int[] PhyTypes;

		[MarshalAs(UnmanagedType.Bool)]
		public bool MorePhyTypes;

		public uint SignalQuality;

		[MarshalAs(UnmanagedType.Bool)]
		public bool SecurityEnabled;

		public int DefaultAuthAlgorithm;

		public int DefaultCipherAlgorithm;

		public uint Flags;

		public uint Reserved;
	}

	private enum WlanConnectionMode
	{
		Profile,
		TemporaryProfile,
		DiscoverySecure,
		DiscoveryUnsecure,
		Auto,
		Invalid
	}

	private enum Dot11BssType
	{
		Infrastructure = 1,
		Independent,
		Any
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WlanConnectionParameters
	{
		public WlanConnectionMode ConnectionMode;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Profile;

		public nint DesiredSsid;

		public nint DesiredBssidList;

		public Dot11BssType BssType;

		public uint Flags;
	}

	public static async Task<IReadOnlyList<WifiNetworkOption>> ScanAndGetAvailableNetworksAsync()
	{
		await Task.Run((Action)RequestScan);
		await Task.Delay(1200);
		return await Task.Run((Func<IReadOnlyList<WifiNetworkOption>>)GetAvailableNetworks);
	}

	public static IReadOnlyList<WifiNetworkOption> GetAvailableNetworks()
	{
		nint clientHandle = 0;
		nint interfaceList = 0;
		try
		{
			if (WlanOpenHandle(2u, 0, out var _, out clientHandle) != 0 || clientHandle == 0)
			{
				return Array.Empty<WifiNetworkOption>();
			}
			if (WlanEnumInterfaces(clientHandle, 0, out interfaceList) != 0 || interfaceList == 0)
			{
				return Array.Empty<WifiNetworkOption>();
			}
			int num = Marshal.ReadInt32(interfaceList);
			int num2 = 8;
			int num3 = Marshal.SizeOf<WlanInterfaceInfo>();
			Dictionary<string, WifiNetworkOption> dictionary = new Dictionary<string, WifiNetworkOption>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < num; i++)
			{
				WlanInterfaceInfo wlanInterfaceInfo = Marshal.PtrToStructure<WlanInterfaceInfo>(IntPtr.Add(interfaceList, num2 + i * num3));
				AppendNetworks(clientHandle, wlanInterfaceInfo.InterfaceGuid, dictionary);
			}
			return (from network in dictionary.Values
				orderby network.IsConnected descending, network.SignalQuality descending
				select network).ThenBy((WifiNetworkOption network) => network.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is AccessViolationException || ex is SEHException) ? true : false)
		{
			return Array.Empty<WifiNetworkOption>();
		}
		finally
		{
			if (interfaceList != 0)
			{
				WlanFreeMemory(interfaceList);
			}
			if (clientHandle != 0)
			{
				WlanCloseHandle(clientHandle, 0);
			}
		}
	}

	public static bool Connect(WifiNetworkOption network)
	{
		if (!network.HasSavedProfile || string.IsNullOrWhiteSpace(network.ProfileName))
		{
			return false;
		}
		nint clientHandle = 0;
		try
		{
			if (WlanOpenHandle(2u, 0, out var _, out clientHandle) != 0 || clientHandle == 0)
			{
				return false;
			}
			WlanConnectionParameters connectionParameters = new WlanConnectionParameters
			{
				ConnectionMode = WlanConnectionMode.Profile,
				Profile = network.ProfileName,
				DesiredSsid = 0,
				DesiredBssidList = 0,
				BssType = Dot11BssType.Any,
				Flags = 0u
			};
			Guid interfaceGuid = network.InterfaceId;
			return WlanConnect(clientHandle, ref interfaceGuid, ref connectionParameters, 0) == 0;
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is SEHException) ? true : false)
		{
			return false;
		}
		finally
		{
			if (clientHandle != 0)
			{
				WlanCloseHandle(clientHandle, 0);
			}
		}
	}

	public static bool Disconnect(WifiNetworkOption network)
	{
		nint clientHandle = 0;
		try
		{
			if (WlanOpenHandle(2u, 0, out var _, out clientHandle) != 0 || clientHandle == 0)
			{
				return false;
			}
			Guid interfaceGuid = network.InterfaceId;
			return WlanDisconnect(clientHandle, ref interfaceGuid, 0) == 0;
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is SEHException) ? true : false)
		{
			return false;
		}
		finally
		{
			if (clientHandle != 0)
			{
				WlanCloseHandle(clientHandle, 0);
			}
		}
	}

	private static void RequestScan()
	{
		nint clientHandle = 0;
		nint interfaceList = 0;
		try
		{
			if (WlanOpenHandle(2u, 0, out var _, out clientHandle) == 0 && clientHandle != 0 && WlanEnumInterfaces(clientHandle, 0, out interfaceList) == 0 && interfaceList != 0)
			{
				int num = Marshal.ReadInt32(interfaceList);
				int num2 = 8;
				int num3 = Marshal.SizeOf<WlanInterfaceInfo>();
				for (int i = 0; i < num; i++)
				{
					Guid interfaceGuid = Marshal.PtrToStructure<WlanInterfaceInfo>(IntPtr.Add(interfaceList, num2 + i * num3)).InterfaceGuid;
					WlanScan(clientHandle, ref interfaceGuid, 0, 0, 0);
				}
			}
		}
		catch (Exception ex) when ((ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is SEHException) ? true : false)
		{
		}
		finally
		{
			if (interfaceList != 0)
			{
				WlanFreeMemory(interfaceList);
			}
			if (clientHandle != 0)
			{
				WlanCloseHandle(clientHandle, 0);
			}
		}
	}

	private static void AppendNetworks(nint client, Guid interfaceId, Dictionary<string, WifiNetworkOption> results)
	{
		nint availableNetworkList = 0;
		try
		{
			if (WlanGetAvailableNetworkList(client, ref interfaceId, 0u, 0, out availableNetworkList) != 0 || availableNetworkList == 0)
			{
				return;
			}
			int num = Marshal.ReadInt32(availableNetworkList);
			int num2 = 8;
			int num3 = Marshal.SizeOf<WlanAvailableNetwork>();
			for (int i = 0; i < num; i++)
			{
				WlanAvailableNetwork wlanAvailableNetwork = Marshal.PtrToStructure<WlanAvailableNetwork>(IntPtr.Add(availableNetworkList, num2 + i * num3));
				string text = DecodeSsid(wlanAvailableNetwork.Ssid);
				if (!string.IsNullOrWhiteSpace(text))
				{
					WifiNetworkOption wifiNetworkOption = new WifiNetworkOption(interfaceId, text, wlanAvailableNetwork.ProfileName ?? string.Empty, (int)Math.Clamp(wlanAvailableNetwork.SignalQuality, 0u, 100u), (wlanAvailableNetwork.Flags & 1) != 0, (wlanAvailableNetwork.Flags & 2) != 0 && !string.IsNullOrWhiteSpace(wlanAvailableNetwork.ProfileName));
					if (!results.TryGetValue(text, out WifiNetworkOption value) || wifiNetworkOption.IsConnected || wifiNetworkOption.SignalQuality > value.SignalQuality)
					{
						results[text] = wifiNetworkOption;
					}
				}
			}
		}
		finally
		{
			if (availableNetworkList != 0)
			{
				WlanFreeMemory(availableNetworkList);
			}
		}
	}

	private static string DecodeSsid(Dot11Ssid ssid)
	{
		if (ssid.Bytes == null || ssid.Length == 0)
		{
			return string.Empty;
		}
		int count = Math.Min((int)ssid.Length, ssid.Bytes.Length);
		try
		{
			return Encoding.UTF8.GetString(ssid.Bytes, 0, count).TrimEnd('\0');
		}
		catch (DecoderFallbackException)
		{
			return Encoding.Default.GetString(ssid.Bytes, 0, count).TrimEnd('\0');
		}
	}

	[DllImport("wlanapi.dll")]
	private static extern uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint clientHandle);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanCloseHandle(nint clientHandle, nint reserved);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanEnumInterfaces(nint clientHandle, nint reserved, out nint interfaceList);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanGetAvailableNetworkList(nint clientHandle, ref Guid interfaceGuid, uint flags, nint reserved, out nint availableNetworkList);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanScan(nint clientHandle, ref Guid interfaceGuid, nint dot11Ssid, nint ieData, nint reserved);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanConnect(nint clientHandle, ref Guid interfaceGuid, ref WlanConnectionParameters connectionParameters, nint reserved);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanDisconnect(nint clientHandle, ref Guid interfaceGuid, nint reserved);

	[DllImport("wlanapi.dll")]
	private static extern void WlanFreeMemory(nint memory);
}
