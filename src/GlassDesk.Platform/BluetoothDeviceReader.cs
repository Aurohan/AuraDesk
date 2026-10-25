using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace GlassDesk.Platform;

public static class BluetoothDeviceReader
{
	public static async Task<IReadOnlyList<BluetoothDeviceOption>> GetPairedDevicesAsync()
	{
		_ = 4;
		try
		{
			Task<DeviceInformationCollection> classicTask = DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(pairingState: true)).AsTask();
			Task<DeviceInformationCollection> lowEnergyTask = DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(pairingState: true)).AsTask();
			InlineArray2<Task<DeviceInformationCollection>> buffer = default;
			buffer[0] = classicTask;
			buffer[1] = lowEnergyTask;
			await Task.WhenAll<DeviceInformationCollection>(buffer);
			IEnumerable<(DeviceInformation Info, bool LowEnergy)> first = (await classicTask).Select((DeviceInformation item) => (Info: item, LowEnergy: false));
			(DeviceInformation, bool)[] array = first.Concat((await lowEnergyTask).Select((DeviceInformation item) => (Info: item, LowEnergy: true))).DistinctBy(((DeviceInformation Info, bool LowEnergy) candidate) => candidate.Info.Id).Take(30)
				.ToArray();
			List<BluetoothDeviceOption> results = new List<BluetoothDeviceOption>();
			(DeviceInformation Info, bool LowEnergy)[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				(DeviceInformation, bool) tuple = array2[num];
				DeviceInformation info = tuple.Item1;
				IDisposable device = null;
				try
				{
					bool isConnected;
					if (tuple.Item2)
					{
						BluetoothLEDevice bluetoothLEDevice = await BluetoothLEDevice.FromIdAsync(info.Id);
						device = bluetoothLEDevice;
						isConnected = (object)bluetoothLEDevice != null && bluetoothLEDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
					}
					else
					{
						BluetoothDevice bluetoothDevice = await BluetoothDevice.FromIdAsync(info.Id);
						device = bluetoothDevice;
						isConnected = (object)bluetoothDevice != null && bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
					}
					results.Add(new BluetoothDeviceOption(info.Id, string.IsNullOrWhiteSpace(info.Name) ? "蓝牙设备" : info.Name, isConnected, IsPaired: true));
				}
				catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is COMException) ? true : false)
				{
					results.Add(new BluetoothDeviceOption(info.Id, string.IsNullOrWhiteSpace(info.Name) ? "蓝牙设备" : info.Name, IsConnected: false, IsPaired: true));
				}
				finally
				{
					device?.Dispose();
				}
			}
			return (from @group in results.GroupBy((BluetoothDeviceOption bluetoothDeviceOption) => bluetoothDeviceOption.Name, StringComparer.CurrentCultureIgnoreCase)
				select @group.OrderByDescending((BluetoothDeviceOption bluetoothDeviceOption) => bluetoothDeviceOption.IsConnected).First() into bluetoothDeviceOption
				orderby bluetoothDeviceOption.IsConnected descending
				select bluetoothDeviceOption).ThenBy((BluetoothDeviceOption bluetoothDeviceOption) => bluetoothDeviceOption.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
		}
		catch (Exception ex2) when ((ex2 is UnauthorizedAccessException || ex2 is InvalidOperationException || ex2 is PlatformNotSupportedException || ex2 is TypeLoadException || ex2 is COMException) ? true : false)
		{
			return Array.Empty<BluetoothDeviceOption>();
		}
	}
}
