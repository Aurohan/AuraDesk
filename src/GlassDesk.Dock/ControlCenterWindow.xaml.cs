using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using GlassDesk.Core.Models.Settings;
using GlassDesk.Platform;
using GlassDesk.Platform.Displays;
using GlassDesk.UI.Controls;

namespace GlassDesk.Dock;

public partial class ControlCenterWindow : GlassWindow, IComponentConnector
{
	private readonly DevicePickerWindow _devicePicker = new DevicePickerWindow();

	private readonly PopupRevealAnimation _reveal;

	private bool _refreshing;

	private bool _closed;

	private long _generation;

	private PixelRect _anchor;

	private Func<bool>? _isGameSleepEnabled;

	private Action<bool>? _setGameSleepEnabled;

	private Action? _showGlassDeskSettings;

	public event EventHandler<MediaSessionSnapshot>? MediaSnapshotChanged;

	public ControlCenterWindow()
	{
		InitializeComponent();
		_reveal = new PopupRevealAnimation(this, PopupContent);
		PanelCornerRadius = new CornerRadius(30.0);
		GlassSurface.IsBackdropActive = () => IsVisible && !_closed;
		MediaPlayer.SnapshotChanged += (object? _, MediaSessionSnapshot snapshot) =>
		{
			MediaSnapshotChanged?.Invoke(this, snapshot);
		};
		_devicePicker.IsVisibleChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			UpdateArrows();
		};
		_devicePicker.OutputChanged += (object? _, EventArgs _) =>
		{
			if (!_closed && IsVisible)
			{
				RefreshAsync();
			}
		};
		Deactivated += (object? _, EventArgs _) =>
		{
			Hide();
		};
		IsVisibleChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			if (!IsVisible)
			{
				_generation++;
				_devicePicker.Hide();
			}
			UpdateArrows();
		};
		PreviewKeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Escape)
			{
				if (_devicePicker.IsVisible)
				{
					_devicePicker.Hide();
				}
				else
				{
					Hide();
				}
				e.Handled = true;
			}
		};
	}

	protected override double ResolveCornerRadius(AppearanceSettings appearance)
	{
		return 30.0;
	}

	internal void StartMedia()
	{
		MediaPlayer.Start();
	}

	internal void StopMedia()
	{
		MediaPlayer.Stop();
	}

	internal void ConfigureGameSleep(Func<bool> isEnabled, Action<bool> setEnabled)
	{
		_isGameSleepEnabled = isEnabled;
		_setGameSleepEnabled = setEnabled;
		RefreshGameSleepControl();
	}

	internal void ConfigureSettings(Action showSettings)
	{
		_showGlassDeskSettings = showSettings;
		SettingsButton.IsEnabled = true;
	}

	internal void RequestGlassRefresh()
	{
		if (IsVisible)
		{
			GlassSurface.RequestBackdropRefresh();
		}
		_devicePicker.RequestGlassRefresh();
	}

	public void ShowNear(PixelRect anchor)
	{
		if (_closed)
		{
			return;
		}
		_anchor = anchor;
		bool flag = !IsVisible;
		if (flag)
		{
			new WindowInteropHelper(this).EnsureHandle();
		}
		PixelRect pixelRect = PositionNearAnchor();
		if (flag)
		{
			PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
			if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable)
			{
				GlassSurface.PrepareRevealMaterial(pixelRect, pixelRect, primaryDisplaySession.Display.Dpi);
			}
			_reveal.Prepare();
			Show();
		}
		Activate();
		GlassSurface.RequestBackdropRefresh(revealing: true);
		RefreshAsync();
	}

	private PixelRect PositionNearAnchor()
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null || !primaryDisplaySession.IsAvailable)
		{
			return default;
		}
		DisplayInfo display = primaryDisplaySession.Display;
		int num = display.ToPixels(Width);
		int num2 = display.ToPixels(Height);
		int num3 = display.ToPixels(16.0);
		int num4 = _anchor.Bottom + num3;
		int num5 = _anchor.Y - num3 - num2;
		int y = ((num4 + num2 <= display.WorkArea.Bottom) ? num4 : num5);
		return PlaceWindowWithinPrimaryWorkArea(new PixelRect(_anchor.Right - num, y, num, num2));
	}

	private async Task RefreshAsync()
	{
		long generation = ++_generation;
		Task<(SystemStatusSnapshot Status, AudioOutputDevice Output)> statusTask = Task.Run(() => (Status: SystemStatusReader.Read(), Output: AudioOutputController.GetDevices().FirstOrDefault((AudioOutputDevice device) => device.IsDefault)));
		Task<SystemRadioState> wifiTask = SystemRadioController.ReadWifiAsync();
		Task<SystemRadioState> bluetoothTask = SystemRadioController.ReadBluetoothAsync();
		InlineArray3<Task> buffer = default;
		buffer[0] = statusTask;
		buffer[1] = wifiTask;
		buffer[2] = bluetoothTask;
		await Task.WhenAll(buffer);
		if (_closed || Dispatcher.HasShutdownStarted)
		{
			return;
		}
		await Dispatcher.InvokeAsync(() =>
		{
			if (_closed || !IsVisible || generation != _generation)
			{
				return;
			}
			_refreshing = true;
			try
			{
				RefreshGameSleepControl();
				var (systemStatusSnapshot, audioOutputDevice) = statusTask.Result;
				VolumeSlider.Value = systemStatusSnapshot.VolumePercent.GetValueOrDefault();
				TextBlock volumeValueText = VolumeValueText;
				int? volumePercent = systemStatusSnapshot.VolumePercent;
				string text;
				if (volumePercent.HasValue)
				{
					int valueOrDefault = volumePercent.GetValueOrDefault();
					text = $"{valueOrDefault}%";
				}
				else
				{
					text = "--";
				}
				volumeValueText.Text = text;
				VolumeGlyph.Text = (systemStatusSnapshot.IsMuted ? "\ue74f" : "\ue767");
				OutputDeviceNameText.Text = audioOutputDevice?.Name ?? "没有可用的输出设备";
				OutputDeviceNameText.ToolTip = OutputDeviceNameText.Text;
				ApplyRadioState(wifiTask.Result, WifiStateText, WifiIconBubble);
				ApplyRadioState(bluetoothTask.Result, BluetoothStateText, BluetoothIconBubble);
				RefreshActiveAudioApps();
			}
			finally
			{
				_refreshing = false;
			}
		});
	}

	private void RefreshActiveAudioApps()
	{
		try
		{
			var apps = ActiveAudioApplicationReader.ReadAll();
			AppVolumesContainer.Children.Clear();
			if (apps.Count == 0)
			{
				AppVolumesContainer.Visibility = Visibility.Collapsed;
				return;
			}

			AppVolumesContainer.Children.Add(new System.Windows.Shapes.Rectangle
			{
				Height = 1,
				Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
				Margin = new Thickness(0, 4, 0, 4)
			});

			foreach (var app in apps)
			{
				var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

				var nameText = new TextBlock
				{
					Text = app.ApplicationName,
					ToolTip = app.Title,
					FontSize = 10,
					Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
					VerticalAlignment = VerticalAlignment.Center,
					TextTrimming = TextTrimming.CharacterEllipsis
				};
				Grid.SetColumn(nameText, 0);
				row.Children.Add(nameText);

				var muteBtn = new Button
				{
					Content = new TextBlock
					{
						Text = app.IsMuted ? "\ue74f" : "\ue767",
						FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
						FontSize = 12,
						Foreground = Brushes.White
					},
					Background = Brushes.Transparent,
					BorderThickness = new Thickness(0),
					Cursor = Cursors.Hand,
					Width = 24,
					Height = 24
				};
				Grid.SetColumn(muteBtn, 1);
				row.Children.Add(muteBtn);

				var slider = new Slider
				{
					Minimum = 0,
					Maximum = 100,
					Value = app.VolumePercent,
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(4, 0, 4, 0)
				};
				Grid.SetColumn(slider, 2);
				row.Children.Add(slider);

				var valText = new TextBlock
				{
					Text = $"{app.VolumePercent}%",
					FontSize = 9.5,
					Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
					VerticalAlignment = VerticalAlignment.Center,
					HorizontalAlignment = HorizontalAlignment.Right
				};
				Grid.SetColumn(valText, 3);
				row.Children.Add(valText);

				int pid = app.ProcessId;
				muteBtn.Click += (_, _) =>
				{
					bool newMute = !app.IsMuted;
					ActiveAudioApplicationReader.SetApplicationMute(pid, newMute);
					RefreshAsync();
				};

				slider.ValueChanged += (_, e) =>
				{
					int newVol = (int)Math.Round(e.NewValue);
					valText.Text = $"{newVol}%";
					ActiveAudioApplicationReader.SetApplicationVolume(pid, newVol);
				};

				AppVolumesContainer.Children.Add(row);
			}

			AppVolumesContainer.Visibility = Visibility.Visible;
		}
		catch
		{
			AppVolumesContainer.Visibility = Visibility.Collapsed;
		}
	}

	private static void ApplyRadioState(SystemRadioState state, TextBlock label, Border bubble)
	{
		TextBlock textBlock = label;
		string text;
		if (state.Available)
		{
			text = (state.IsOn ? "已开启" : "已关闭");
		}
		else
		{
			text = "打开设置";
		}
		textBlock.Text = text;
		bubble.Background = new SolidColorBrush((state.Available && state.IsOn) ? Color.FromRgb(65, 145, byte.MaxValue) : Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue));
	}

	private void OnGameSleepClick(object sender, RoutedEventArgs e)
	{
		if (_isGameSleepEnabled != null && _setGameSleepEnabled != null)
		{
			_setGameSleepEnabled(!_isGameSleepEnabled());
			RefreshGameSleepControl();
		}
	}

	private void OnSettingsClick(object sender, RoutedEventArgs e)
	{
		_showGlassDeskSettings?.Invoke();
	}

	private void RefreshGameSleepControl()
	{
		bool flag = _isGameSleepEnabled != null && _setGameSleepEnabled != null;
		bool flag2 = flag && _isGameSleepEnabled();
		GameSleepButton.IsEnabled = flag;
		TextBlock gameSleepStateText = GameSleepStateText;
		string text;
		if (flag)
		{
			text = (flag2 ? "已开启 · 快捷键或托盘菜单恢复" : "开启后隐藏桌面组件");
		}
		else
		{
			text = "当前不可用";
		}
		gameSleepStateText.Text = text;
		GameSleepIconBubble.Background = new SolidColorBrush(flag2 ? Color.FromRgb(81, 156, byte.MaxValue) : Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue));
	}

	private void OnWifiClick(object sender, RoutedEventArgs e)
	{
		TogglePicker(DevicePickerKind.Wifi, WifiButton);
	}

	private void OnBluetoothClick(object sender, RoutedEventArgs e)
	{
		TogglePicker(DevicePickerKind.Bluetooth, BluetoothButton);
	}

	private void OnOutputDeviceClick(object sender, RoutedEventArgs e)
	{
		TogglePicker(DevicePickerKind.AudioOutput, OutputDeviceArrowButton);
	}

	private void TogglePicker(DevicePickerKind kind, FrameworkElement button)
	{
		if (_closed || !IsVisible)
		{
			return;
		}
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null || !primaryDisplaySession.IsAvailable)
		{
			return;
		}
		if (_devicePicker.IsVisible && _devicePicker.Kind == kind)
		{
			_devicePicker.Hide();
			return;
		}
		if (_devicePicker.PrimaryDisplaySession == null)
		{
			_devicePicker.BindPrimaryDisplay(primaryDisplaySession);
		}
		DevicePickerWindow devicePicker = _devicePicker;
		if (devicePicker.Owner == null)
		{
			Window window = (devicePicker.Owner = this);
		}
		_devicePicker.Icon = Icon;
		Point point = button.PointToScreen(new Point(0.0, button.ActualHeight));
		_devicePicker.ShowNear(kind, new PixelRect((int)point.X, (int)point.Y, (int)Math.Max(1.0, button.ActualWidth * (double)primaryDisplaySession.Display.Dpi / 96.0), 0));
		UpdateArrows();
	}

	private void UpdateArrows()
	{
		UpdateArrow(WifiButton, WifiArrowGlyph, DevicePickerKind.Wifi, "Wi-Fi 网络");
		UpdateArrow(BluetoothButton, BluetoothArrowGlyph, DevicePickerKind.Bluetooth, "蓝牙设备");
		UpdateArrow(OutputDeviceArrowButton, OutputDeviceArrowGlyph, DevicePickerKind.AudioOutput, "输出设备");
	}

	private void UpdateArrow(Button button, TextBlock glyph, DevicePickerKind kind, string label)
	{
		bool flag = _devicePicker.IsVisible && _devicePicker.Kind == kind;
		glyph.Text = (flag ? "\ue70e" : "\ue70d");
		button.ToolTip = (flag ? "收起" : "展开") + label;
		AutomationProperties.SetName(button, (string)button.ToolTip);
	}

	private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (!_refreshing)
		{
			int num = (int)Math.Round(e.NewValue);
			SystemStatusReader.SetVolumePercent(num);
			if (num > 0)
			{
				SystemStatusReader.SetMuted(muted: false);
			}
			VolumeValueText.Text = $"{num}%";
			VolumeGlyph.Text = ((num == 0) ? "\ue74f" : "\ue767");
		}
	}

	private void OnMuteClick(object sender, RoutedEventArgs e)
	{
		SystemStatusReader.SetMuted(!SystemStatusReader.Read().IsMuted);
		RefreshAsync();
	}

	protected override void OnClosed(EventArgs e)
	{
		_closed = true;
		_generation++;
		_devicePicker.Close();
		MediaPlayer.Dispose();
		GlassSurface.Dispose();
		base.OnClosed(e);
	}

	

    private void OnLockScreenClick(object sender, RoutedEventArgs e)
    {
        Hide();
        GlassDesk.Platform.Shell.PowerControl.Lock();
    }

    private void OnSleepClick(object sender, RoutedEventArgs e)
    {
        Hide();
        GlassDesk.Platform.Shell.PowerControl.Sleep();
    }

    private void OnRestartClick(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(this, "确定要立即重启计算机吗？请确保未保存的工作已暂存。", "系统重启确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            Hide();
            GlassDesk.Platform.Shell.PowerControl.Restart();
        }
    }

    private void OnShutdownClick(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(this, "确定要立即关闭计算机吗？请确保未保存的工作已暂存。", "系统关机确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            Hide();
            GlassDesk.Platform.Shell.PowerControl.Shutdown();
        }
    }
}
