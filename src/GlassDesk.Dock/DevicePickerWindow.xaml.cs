using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using GlassDesk.Core.Models.Settings;
using GlassDesk.Platform;
using GlassDesk.Platform.Displays;
using GlassDesk.Platform.Shell;
using GlassDesk.UI.Controls;

namespace GlassDesk.Dock;

public partial class DevicePickerWindow : GlassWindow, IComponentConnector
{
	private sealed record PickerItem(string Title, string Subtitle, string Glyph, string Action, object? Value);

	private long _generation;

	private bool _closed;

	private HwndSource? _source;

	private readonly PopupRevealAnimation _reveal;

	public DevicePickerKind Kind { get; private set; }

	public event EventHandler? OutputChanged;

	public DevicePickerWindow()
	{
		InitializeComponent();
		_reveal = new PopupRevealAnimation(this, PopupContent);
		PanelCornerRadius = new CornerRadius(23.0);
		GlassSurface.IsBackdropActive = () => IsVisible && !_closed;
		IsVisibleChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			if (!IsVisible)
			{
				_generation++;
			}
		};
	}

	protected override double ResolveCornerRadius(AppearanceSettings appearance)
	{
		return 23.0;
	}

	public void ShowNear(DevicePickerKind kind, PixelRect anchor)
	{
		if (_closed)
		{
			return;
		}
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable)
		{
			Kind = kind;
			TextBlock selectorTitle = SelectorTitle;
			string title = (selectorTitle.Text = kind switch
			{
				DevicePickerKind.Wifi => "Wi-Fi 网络", 
				DevicePickerKind.Bluetooth => "蓝牙设备", 
				_ => "声音输出设备", 
			});
			Title = title;
			RadioPowerButton.Visibility = ((kind == DevicePickerKind.AudioOutput) ? Visibility.Collapsed : Visibility.Visible);
			Button selectorSettingsButton = SelectorSettingsButton;
			selectorSettingsButton.Content = kind switch
			{
				DevicePickerKind.Wifi => (object)"打开 Wi-Fi 设置", 
				DevicePickerKind.Bluetooth => "打开蓝牙设置", 
				_ => "打开声音设置", 
			};
			DisplayInfo display = primaryDisplaySession.Display;
			bool flag = !IsVisible;
			if (flag)
			{
				new WindowInteropHelper(this).EnsureHandle();
			}
			PixelRect pixelRect = PlaceWindowWithinPrimaryWorkArea(new PixelRect(anchor.Right - display.ToPixels(Width), anchor.Bottom + display.ToPixels(8.0), display.ToPixels(Width), display.ToPixels(Height)));
			if (flag)
			{
				GlassSurface.PrepareRevealMaterial(pixelRect, pixelRect, display.Dpi);
				_reveal.Prepare();
				Show();
			}
			GlassSurface.RequestBackdropRefresh(revealing: true);
			RefreshAsync();
		}
	}

	public void RequestGlassRefresh()
	{
		if (IsVisible)
		{
			GlassSurface.RequestBackdropRefresh();
		}
	}

	private async Task RefreshAsync()
	{
		long generation = ++_generation;
		DevicePickerKind kind = Kind;
		SelectorList.ItemsSource = new PickerItem[1]
		{
			new PickerItem("正在读取…", "", "\ue895", "", null)
		};
		IReadOnlyList<PickerItem> items;
		try
		{
			switch (kind)
			{
			case DevicePickerKind.Wifi:
				items = (await WifiNetworkController.ScanAndGetAvailableNetworksAsync().WaitAsync(TimeSpan.FromSeconds(10L))).Select((WifiNetworkOption network) =>
				{
					string name = network.Name;
					string text = (network.IsConnected ? $"已连接 · 信号 {network.SignalQuality}%" : (network.HasSavedProfile ? $"已保存 · 信号 {network.SignalQuality}%" : $"需要密码 · 信号 {network.SignalQuality}%"));
					string title = name;
					string subtitle = text;
					string action = (network.IsConnected ? "断开" : (network.HasSavedProfile ? "连接" : "设置"));
					return new PickerItem(title, subtitle, "\ue701", action, network);
				}).ToArray();
				break;
			case DevicePickerKind.Bluetooth:
				items = (await BluetoothDeviceReader.GetPairedDevicesAsync().WaitAsync(TimeSpan.FromSeconds(10L))).Select((BluetoothDeviceOption device) => new PickerItem(device.Name, device.IsConnected ? "已连接" : "已配对", "\ue702", "管理", device)).ToArray();
				break;
			default:
				items = (await Task.Run((Func<IReadOnlyList<AudioOutputDevice>>)AudioOutputController.GetDevices)).Select((AudioOutputDevice device) => new PickerItem(device.Name, device.IsDefault ? "当前输出设备" : "切换到此设备", "\ue767", device.IsDefault ? "✓" : "选择", device)).ToArray();
				break;
			}
		}
		catch (Exception ex) when ((ex is TimeoutException || ex is UnauthorizedAccessException || ex is COMException) ? true : false)
		{
			items = new[] { new PickerItem("暂时无法读取设备", "可刷新列表或打开系统设置", "\ue711", "设置", null) };
		}
		if (_closed || Dispatcher.HasShutdownStarted)
		{
			return;
		}
		await Dispatcher.InvokeAsync(() =>
		{
			if (generation == _generation && IsVisible && !_closed)
			{
				ListBox selectorList = SelectorList;
				IReadOnlyList<PickerItem> itemsSource;
				if (items.Count <= 0)
				{
					IReadOnlyList<PickerItem> readOnlyList = new PickerItem[1]
					{
						new PickerItem("没有可用设备", "可在 Windows 设置中检查设备和权限", "\ue711", "设置", null)
					};
					itemsSource = readOnlyList;
				}
				else
				{
					itemsSource = items;
				}
				selectorList.ItemsSource = itemsSource;
			}
		});
	}

	private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource is DependencyObject element && ItemsControl.ContainerFromElement(SelectorList, element) is ListBoxItem listBoxItem)
		{
			e.Handled = true;
			SelectorList.SelectedItem = listBoxItem.Content;
		}
	}

	private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(SelectorList.SelectedItem is PickerItem pickerItem))
		{
			return;
		}
		SelectorList.SelectedItem = null;
		long generation = _generation;
		object value = pickerItem.Value;
		AudioOutputDevice output = value as AudioOutputDevice;
		if ((object)output != null)
		{
			if (!(await Task.Run(() => AudioOutputController.SetDefault(output.Id))))
			{
				if (IsVisible && generation == _generation)
				{
					SelectorList.ItemsSource = new PickerItem[1]
					{
						new PickerItem(output.Name, "切换未成功，请重试或打开声音设置", "\ue711", "设置", null)
					};
				}
				return;
			}
			OutputChanged?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			value = pickerItem.Value;
			WifiNetworkOption network = value as WifiNetworkOption;
			if ((object)network == null)
			{
				if (pickerItem.Value is BluetoothDeviceOption || pickerItem.Action == "设置")
				{
					OnSettingsClick(sender, new RoutedEventArgs());
				}
				return;
			}
			if (!(await Task.Run(() => (!network.IsConnected) ? (network.HasSavedProfile && WifiNetworkController.Connect(network)) : WifiNetworkController.Disconnect(network))))
			{
				OnSettingsClick(sender, new RoutedEventArgs());
				return;
			}
			await Task.Delay(1000);
		}
		if (!_closed && IsVisible && generation == _generation)
		{
			await RefreshAsync();
		}
	}

	private async void OnRadioPowerClick(object sender, RoutedEventArgs e)
	{
		bool wifi = Kind == DevicePickerKind.Wifi;
		SystemRadioState systemRadioState = ((!wifi) ? (await SystemRadioController.ReadBluetoothAsync()) : (await SystemRadioController.ReadWifiAsync()));
		SystemRadioState systemRadioState2 = systemRadioState;
		bool flag = systemRadioState2.Available && systemRadioState2.CanControl;
		if (flag)
		{
			bool flag2 = ((!wifi) ? (await SystemRadioController.SetBluetoothAsync(!systemRadioState2.IsOn)) : (await SystemRadioController.SetWifiAsync(!systemRadioState2.IsOn)));
			flag = flag2;
		}
		if (!flag)
		{
			OnSettingsClick(sender, new RoutedEventArgs());
		}
		if (!_closed && IsVisible)
		{
			await RefreshAsync();
		}
	}

	private async void OnRefreshClick(object sender, RoutedEventArgs e)
	{
		await RefreshAsync();
	}

	private void OnSettingsClick(object sender, RoutedEventArgs e)
	{
		ShellExecutor.Open(Kind switch
		{
			DevicePickerKind.Wifi => "ms-settings:network-wifi", 
			DevicePickerKind.Bluetooth => "ms-settings:bluetooth", 
			_ => "ms-settings:sound", 
		});
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint handle = new WindowInteropHelper(this).Handle;
		WindowStyles.MakeNoActivate(handle);
		_source = HwndSource.FromHwnd(handle);
		_source?.AddHook(KeepOwnerActive);
	}

	private static nint KeepOwnerActive(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
	{
		if (message != 33)
		{
			return 0;
		}
		handled = true;
		return 3;
	}

	protected override void OnClosed(EventArgs e)
	{
		_closed = true;
		_generation++;
		_source?.RemoveHook(KeepOwnerActive);
		_source = null;
		GlassSurface.Dispose();
		base.OnClosed(e);
	}
}
