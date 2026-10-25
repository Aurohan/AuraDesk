using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GlassDesk.Core.Models.Settings;
using GlassDesk.Platform;
using GlassDesk.Platform.Displays;
using GlassDesk.Platform.Shell;
using GlassDesk.UI.Controls;
using GlassDesk.UI.Settings;
using GlassDesk.UI.Theming;

namespace GlassDesk.Dock;

public partial class StatusBarWindow : GlassWindow, IComponentConnector
{
	private readonly ControlCenterWindow _controlCenter = new ControlCenterWindow();

	private readonly DateNotificationWindow _dateNotifications = new DateNotificationWindow();

	private bool _companionsRunning;

	private const double BaseWidthDip = 380.0;

	private const double BaseHeightDip = 54.0;

	private const double BaseCornerRadiusDip = 25.0;

	private readonly DispatcherTimer _clockTimer;

	private readonly DispatcherTimer _visibilityTimer;

	private readonly StatusBarVisibilityController _visibility = new StatusBarVisibilityController();

	private DockSettingsHost? _settings;

	private PixelRect _restingRect;

	private bool _started;

	private bool _closed;

	private bool _performanceSleeping;

	private bool _displayPaused;

	private bool _sessionLocked;

	private bool _revealLocked;

	private bool _targetVisible;

	private bool _captureRequested;

	private bool _sampling;

	private bool _fanSampling;

	private bool _preparingReveal;

	private bool? _animationTarget;

	private long _generation;

	private long _preparationStarted;

	private DateTimeOffset _lastSystemRefresh = DateTimeOffset.MinValue;

	private DateTimeOffset _lastFanRefresh = DateTimeOffset.MinValue;

	private Func<(double X, double Y, StatusBarEdge Edge)>? _readPlacement;

	private Func<double>? _readSizeScale;

	private static readonly DependencyProperty RevealProgressProperty = DependencyProperty.Register("RevealProgress", typeof(double), typeof(StatusBarWindow), new PropertyMetadata(0.0, (DependencyObject owner, DependencyPropertyChangedEventArgs _) =>
	{
		((StatusBarWindow)owner).ApplySlide();
	}));

	private bool IsPopoverVisible
	{
		get
		{
			if (!_controlCenter.IsVisible)
			{
				return _dateNotifications.IsVisible;
			}
			return true;
		}
	}

	protected override bool UseStatusBarTextBrightness => true;

	public Func<bool>? IsLauncherActive { get; set; }

	public bool IsPerformanceCaptureRequested
	{
		get
		{
			if (CanRun)
			{
				if (!_targetVisible)
				{
					return IsPopoverVisible;
				}
				return true;
			}
			return false;
		}
	}

	private DockSettings Settings => _settings?.Settings ?? new DockSettings();

	private bool CanRun
	{
		get
		{
			if (_started && !_closed && !_performanceSleeping && !_displayPaused && Settings.ShowStatusBar)
			{
				return PrimaryDisplaySession?.IsAvailable ?? false;
			}
			return false;
		}
	}

	public event EventHandler? PerformanceSurfaceVisibilityChanged;

	public void ConfigureGameSleep(Func<bool> isEnabled, Action<bool> setEnabled)
	{
		_controlCenter.ConfigureGameSleep(isEnabled, setEnabled);
	}

	public void ConfigureSettings(Action showSettings)
	{
		_controlCenter.ConfigureSettings(showSettings);
	}

	private void InitializeCompanions()
	{
		_controlCenter.IsVisibleChanged += OnPopoverVisibilityChanged;
		_dateNotifications.IsVisibleChanged += OnPopoverVisibilityChanged;
		_controlCenter.MediaSnapshotChanged += (object? _, MediaSessionSnapshot snapshot) =>
		{
			MusicTitleText.Text = (snapshot.Available ? snapshot.Title : "暂无媒体");
			MusicTitleText.ToolTip = MusicTitleText.Text;
		};
	}

	private void UpdateCompanions(bool running)
	{
		if (_companionsRunning != running)
		{
			_companionsRunning = running;
			if (running)
			{
				BindCompanionWindows();
				_controlCenter.StartMedia();
			}
			else
			{
				_controlCenter.StopMedia();
				_controlCenter.Hide();
				_dateNotifications.Hide();
			}
		}
	}

	public void RequestGlassRefresh()
	{
		if (IsVisible && CanRun)
		{
			GlassSurface.RequestBackdropRefresh();
		}
		_controlCenter.RequestGlassRefresh();
		_dateNotifications.RequestGlassRefresh();
	}

	private void BindCompanionWindows()
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null)
		{
			return;
		}
		GlassWindow[] array = new GlassWindow[2] { _controlCenter, _dateNotifications };
		foreach (GlassWindow glassWindow in array)
		{
			if (glassWindow.PrimaryDisplaySession == null)
			{
				glassWindow.BindPrimaryDisplay(primaryDisplaySession);
			}
			glassWindow.Icon = Icon;
		}
	}

	private void OnPopoverVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		ControlCenterArrowGlyph.Text = (_controlCenter.IsVisible ? "\ue70e" : "\ue70d");
		if (_companionsRunning)
		{
			EvaluateVisibility();
		}
		NotifyCaptureRequested();
	}

	private static void OpenSettings(string uri)
	{
		ShellExecutor.Open(uri);
	}

	private void OnDateTimeClick(object sender, RoutedEventArgs e)
	{
		if (CanRun)
		{
			if (_dateNotifications.IsVisible)
			{
				_dateNotifications.Hide();
				return;
			}
			_controlCenter.Hide();
			_dateNotifications.ShowNear(_restingRect);
		}
	}

	private void OnBatteryClick(object sender, RoutedEventArgs e)
	{
		OpenSettings("ms-settings:batterysaver");
	}

	private void OnControlCenterArrowClick(object sender, RoutedEventArgs e)
	{
		ToggleControlCenter();
	}

	private void ToggleControlCenter()
	{
		if (CanRun)
		{
			if (_controlCenter.IsVisible)
			{
				_controlCenter.Hide();
				return;
			}
			_dateNotifications.Hide();
			_controlCenter.ShowNear(_restingRect);
		}
	}

	public StatusBarWindow()
	{
		InitializeComponent();
		InitializeCompanions();
		PanelCornerRadius = new CornerRadius(25.0);
		GlassSurface.IsBackdropActive = () => CanRun && !_preparingReveal && IsVisible && (_targetVisible || (double)GetValue(RevealProgressProperty) > 0.0);
		_clockTimer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_clockTimer.Tick += (object? _, EventArgs _) =>
		{
			RefreshClockAndStatus();
		};
		_visibilityTimer = new DispatcherTimer(DispatcherPriority.Send)
		{
			Interval = TimeSpan.FromMilliseconds(100L)
		};
		_visibilityTimer.Tick += (object? _, EventArgs _) =>
		{
			EvaluateVisibility();
		};
		Loaded += (object _, RoutedEventArgs _) =>
		{
			ApplySlide();
		};
		SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			ApplySlide();
		};
		Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
	}

	protected override double ResolveCornerRadius(AppearanceSettings appearance)
	{
		return 25.0;
	}

	public void AttachSettings(DockSettingsHost settings)
	{
		if (_settings != null)
		{
			_settings.Changed -= OnSettingsChanged;
		}
		_settings = settings;
		_settings.Changed += OnSettingsChanged;
		OnSettingsChanged(this, EventArgs.Empty);
	}

	public void Start()
	{
		if (!_closed)
		{
			_started = true;
			RefreshDisplayPlacement();
		}
	}

	public void ConfigurePlacement(Func<(double X, double Y, StatusBarEdge Edge)> readPlacement)
	{
		_readPlacement = readPlacement;
	}

	public void ConfigureSizeScale(Func<double> readSizeScale)
	{
		_readSizeScale = readSizeScale;
	}

	private void OnSettingsChanged(object? sender, EventArgs e)
	{
		ApplySizeScale();
		PositionTopRight();
		EvaluateVisibility();
	}

	public void ToggleRevealLock()
	{
		_revealLocked = !_revealLocked;
		EvaluateVisibility();
	}

	public void SetPerformanceSleeping(bool sleeping)
	{
		if (_performanceSleeping != sleeping)
		{
			_performanceSleeping = sleeping;
			EvaluateVisibility();
		}
	}

	public void StopForPrimaryDisplayChange()
	{
		_displayPaused = true;
		StopImmediately();
	}

	public void RefreshDisplayPlacement()
	{
		_displayPaused = false;
		BindCompanionWindows();
		ApplySizeScale();
		PositionTopRight();
		EvaluateVisibility();
	}

	private void PositionTopRight()
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable)
		{
			DisplayInfo display = primaryDisplaySession.Display;
			int num = display.ToPixels(Width);
			int num2 = display.ToPixels(Height);
			int num3 = display.ToPixels(18.0);
			PixelRect workArea = display.WorkArea;
			(double X, double Y, StatusBarEdge Edge) valueTuple = _readPlacement?.Invoke() ?? (0.0, 0.0, StatusBarEdge.Top);
			double item = valueTuple.X;
			int y = ((valueTuple.Edge == StatusBarEdge.Bottom) ? (workArea.Bottom - num2 - num3 - display.ToPixels(valueTuple.Y)) : (workArea.Y + num3 + display.ToPixels(valueTuple.Y)));
			_restingRect = PlaceWindowWithinPrimaryWorkArea(new PixelRect(workArea.Right - num - num3 + display.ToPixels(item), y, num, num2));
			ApplySlide();
		}
	}

	private void ApplySizeScale()
	{
		double num = Math.Clamp(_readSizeScale?.Invoke() ?? 1.0, 0.75, 1.5);
		Width = Math.Round(380.0 * num, MidpointRounding.AwayFromZero);
		Height = Math.Round(54.0 * num, MidpointRounding.AwayFromZero);
		PanelCornerRadius = new CornerRadius(25.0 * num);
	}

	private void EvaluateVisibility()
	{
		if (!CanRun)
		{
			StopImmediately();
			return;
		}
		if (_restingRect.Width <= 0)
		{
			PositionTopRight();
		}
		if (_restingRect.Width <= 0)
		{
			StopImmediately();
			return;
		}
		_visibilityTimer.Start();
		bool flag = IsLauncherActive?.Invoke() ?? false;
		bool flag2 = FullscreenDetector.IsFullscreenActive();
		UpdateCompanions(!flag2 | flag);
		(int, int)? cursorPixelPoint = DisplayService.GetCursorPixelPoint();
		int num;
		if (cursorPixelPoint.HasValue)
		{
			(int, int) valueOrDefault = cursorPixelPoint.GetValueOrDefault();
			num = (IsOverStatusOrRevealZone(valueOrDefault.Item1, valueOrDefault.Item2) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool isCovered = ForegroundCoverage.IsCovered(_restingRect);
		bool enabled = _settings?.Settings.ShowStatusBar ?? true;
		bool onlyOnDesktop = _settings?.Settings.StatusBarOnlyOnDesktop ?? true;
		bool shouldBeVisible = enabled && !_performanceSleeping && !_displayPaused &&
			((!flag2 && (!onlyOnDesktop || !isCovered)) || flag || IsPopoverVisible);
		SetTargetVisible(shouldBeVisible);
	}

	private bool IsOverStatusOrRevealZone(int x, int y)
	{
		if (new PixelRect(_restingRect.X - 2, _restingRect.Y - 2, _restingRect.Width + 4, _restingRect.Height + 4).Contains(x, y))
		{
			return true;
		}
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null || !primaryDisplaySession.IsAvailable)
		{
			return false;
		}
		PixelRect bounds = primaryDisplaySession.Display.Bounds;
		StatusBarEdge statusBarEdge = _readPlacement?.Invoke().Edge ?? StatusBarEdge.Top;
		int num = primaryDisplaySession.Display.ToPixels(16.0);
		if (x >= bounds.Right - num && x < bounds.Right && ((statusBarEdge == StatusBarEdge.Bottom) ? (y >= bounds.Bottom - num) : (y < bounds.Y + num)))
		{
			return true;
		}
		int num2 = primaryDisplaySession.Display.ToPixels(30.0);
		int num3 = primaryDisplaySession.Display.ToPixels(18.0);
		int num4 = ((statusBarEdge == StatusBarEdge.Bottom) ? _restingRect.Bottom : (_restingRect.Y - num2));
		if (x >= _restingRect.X - num3 && x < _restingRect.Right + num3 && y >= Math.Max(bounds.Y, num4))
		{
			return y < Math.Min(bounds.Bottom, num4 + num2);
		}
		return false;
	}

	private void SetTargetVisible(bool visible)
	{
		bool flag = visible && !_targetVisible && IsVisible && (double)GetValue(RevealProgressProperty) > 0.0;
		if (_targetVisible != visible)
		{
			double num = (double)GetValue(RevealProgressProperty);
			SetValue(RevealProgressProperty, num);
			BeginAnimation(RevealProgressProperty, null);
			_targetVisible = visible;
			_preparationStarted = 0L;
			_animationTarget = null;
			if (!visible)
			{
				StopSampling();
			}
		}
		if (visible)
		{
			GlassSurface.SetPerformanceSleeping(sleeping: false);
			if (flag)
			{
				_preparationStarted = Stopwatch.GetTimestamp();
				AnimateVisibility(visible: true);
			}
			bool? animationTarget;
			if (_preparationStarted == 0L)
			{
				animationTarget = _animationTarget;
				if (!animationTarget.HasValue)
				{
					_preparationStarted = Stopwatch.GetTimestamp();
					_preparingReveal = true;
					try
					{
						new WindowInteropHelper(this).EnsureHandle();
						PositionTopRight();
						GlassSurface.SetContainerTransitionBounds(new Rect(0.0, 0.0 - Height, Width, Height * 2.0));
						PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
						if (primaryDisplaySession != null)
						{
							GlassSurface.PrepareRevealMaterial(_restingRect, _restingRect, primaryDisplaySession.Display.Dpi);
						}
					}
					finally
					{
						_preparingReveal = false;
					}
				}
			}
			if (!IsVisible)
			{
				SetValue(RevealProgressProperty, 0.0);
				Show();
				UpdateLayout();
				PositionTopRight();
			}
			animationTarget = _animationTarget;
			if (!animationTarget.HasValue)
			{
				GlassSurface.RequestBackdropRefresh(revealing: true);
				if (GlassSurface.IsCurrentRevealMaterialReady || Stopwatch.GetElapsedTime(_preparationStarted).TotalSeconds >= 3.0)
				{
					AnimateVisibility(visible: true);
				}
			}
			if (!_clockTimer.IsEnabled)
			{
				_lastSystemRefresh = (_lastFanRefresh = DateTimeOffset.MinValue);
				_clockTimer.Start();
				RefreshClockAndStatus();
			}
		}
		else if (IsVisible && _animationTarget != false)
		{
			AnimateVisibility(visible: false);
		}
		NotifyCaptureRequested();
	}

	private void AnimateVisibility(bool visible)
	{
		double num = (double)GetValue(RevealProgressProperty);
		SetValue(RevealProgressProperty, num);
		BeginAnimation(RevealProgressProperty, null);
		_animationTarget = visible;
		ThemeService? current = ThemeService.Current;
		if (current != null && !current.AnimationsEnabled)
		{
			FinishVisibility(visible);
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(num, visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 150 : 130))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			if (!_closed && _animationTarget == visible && _targetVisible == visible)
			{
				FinishVisibility(visible);
			}
		};
		BeginAnimation(RevealProgressProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
	}

	private void FinishVisibility(bool visible)
	{
		SetValue(RevealProgressProperty, visible ? 1.0 : 0.0);
		BeginAnimation(RevealProgressProperty, null);
		if (!visible)
		{
			GlassSurface.RetireConcealedMaterial();
			Hide();
		}
	}

	private void ApplySlide()
	{
		if (GlassSurface != null && !(ActualWidth <= 0.0) && !(ActualHeight <= 0.0))
		{
			double num = Math.Clamp((double)GetValue(RevealProgressProperty), 0.0, 1.0);
			double y = (0.0 - ActualHeight) * (1.0 - num);
			StatusSlideTransform.Y = y;
			Rect rect = new Rect(0.0, y, ActualWidth, ActualHeight);
			GlassSurface.SetPanelShape(rect, 25.0);
			GlassSurface.SetContainerTransitionBounds(new Rect(0.0, 0.0 - ActualHeight, ActualWidth, ActualHeight * 2.0));
			SetSharedPanelClip(rect, 25.0);
			SetTransitionShadowShape((num >= 1.0) ? ((Rect?)null) : new Rect?(rect), 25.0, num);
		}
	}

	private void StopSampling()
	{
		_clockTimer.Stop();
		_generation++;
	}

	private void StopImmediately()
	{
		UpdateCompanions(running: false);
		_visibilityTimer.Stop();
		StopSampling();
		_targetVisible = false;
		_animationTarget = null;
		_preparationStarted = 0L;
		_visibility.Update(Settings, DateTime.UtcNow, pointerOver: false, covered: false, fullscreen: false, launcherActive: false, revealLocked: false, enabled: false, sleeping: true);
		GlassSurface.SetPerformanceSleeping(sleeping: true);
		FinishVisibility(visible: false);
		NotifyCaptureRequested();
	}

	private void NotifyCaptureRequested()
	{
		bool isPerformanceCaptureRequested = IsPerformanceCaptureRequested;
		if (_captureRequested != isPerformanceCaptureRequested)
		{
			_captureRequested = isPerformanceCaptureRequested;
			PerformanceSurfaceVisibilityChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private void RefreshClockAndStatus()
	{
		if (!CanRun || !_targetVisible || _sessionLocked)
		{
			return;
		}
		DateTimeOffset now = DateTimeOffset.Now;
		TimeText.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
		DateText.Text = now.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (!_sampling)
		{
			bool flag = utcNow - _lastSystemRefresh >= TimeSpan.FromSeconds(5L);
			if (flag)
			{
				_lastSystemRefresh = utcNow;
			}
			RefreshSystemAsync(_generation, flag);
		}
		if (!_fanSampling && utcNow - _lastFanRefresh >= TimeSpan.FromSeconds(8L))
		{
			_lastFanRefresh = utcNow;
			RefreshFanAsync(_generation);
		}
	}

	private bool CanApply(long generation)
	{
		if (generation == _generation && CanRun)
		{
			return _targetVisible;
		}
		return false;
	}

	private async Task RefreshSystemAsync(long generation, bool readSystem)
	{
		_sampling = true;
		try
		{
			(SystemPerformanceSnapshot, SystemStatusSnapshot?) tuple = await Task.Run(() => (Performance: SystemStatusReader.ReadPerformance(), Status: readSystem ? new SystemStatusSnapshot?(SystemStatusReader.Read()) : ((SystemStatusSnapshot?)null)));
			if (CanApply(generation))
			{
				TextBlock cpuText = CpuText;
				int? cpuPercent = tuple.Item1.CpuPercent;
				string text;
				if (cpuPercent.HasValue)
				{
					int valueOrDefault = cpuPercent.GetValueOrDefault();
					text = $"{valueOrDefault}%";
				}
				else
				{
					text = "--";
				}
				cpuText.Text = text;
				TextBlock memoryText = MemoryText;
				cpuPercent = tuple.Item1.MemoryPercent;
				string text2;
				if (cpuPercent.HasValue)
				{
					int valueOrDefault2 = cpuPercent.GetValueOrDefault();
					text2 = $"{valueOrDefault2}%";
				}
				else
				{
					text2 = "--";
				}
				memoryText.Text = text2;
				SystemStatusSnapshot? item = tuple.Item2;
				if (item.HasValue)
				{
					SystemStatusSnapshot valueOrDefault3 = item.GetValueOrDefault();
					ApplySystemStatus(valueOrDefault3);
				}
				if (_settings?.Settings.ShowNetworkSpeed != false && !string.IsNullOrEmpty(tuple.Item1.DownSpeedText))
				{
					NetworkText.Text = $"↓{tuple.Item1.DownSpeedText}";
					NetworkText.ToolTip = $"实时下载: {tuple.Item1.DownSpeedText}\n实时上传: {tuple.Item1.UpSpeedText}\n网络状态: {item?.NetworkLabel ?? "已连接"}";
				}
			}
		}
		catch (Exception)
		{
			if (CanApply(generation))
			{
				TextBlock cpuText2 = CpuText;
				string text3 = (MemoryText.Text = "--");
				cpuText2.Text = text3;
				if (readSystem)
				{
					ApplySystemStatus(new SystemStatusSnapshot(null, IsCharging: false, "未知", null, IsMuted: false));
				}
			}
		}
		finally
		{
			_sampling = false;
		}
	}

	private void ApplySystemStatus(SystemStatusSnapshot status)
	{
		BatteryButton.Visibility = ((!status.HasBattery) ? Visibility.Collapsed : Visibility.Visible);
		TextBlock batteryText = BatteryText;
		int? batteryPercent = status.BatteryPercent;
		object text;
		if (!batteryPercent.HasValue)
		{
			text = (status.IsCharging ? "电源" : "--");
		}
		else
		{
			int valueOrDefault = batteryPercent.GetValueOrDefault();
			text = $"{valueOrDefault}%{(status.IsCharging ? " ⚡" : "")}";
		}
		batteryText.Text = (string)text;
		Border batteryFill = BatteryFill;
		batteryPercent = status.BatteryPercent;
		double width;
		if (batteryPercent.HasValue)
		{
			int valueOrDefault2 = batteryPercent.GetValueOrDefault();
			width = Math.Clamp((double)(14 * valueOrDefault2) / 100.0, 1.0, 14.0);
		}
		else
		{
			width = 0.0;
		}
		batteryFill.Width = width;
		if (_settings?.Settings.ShowNetworkSpeed != true || NetworkText.Text == "--" || string.IsNullOrEmpty(NetworkText.Text))
		{
			NetworkText.Text = status.NetworkLabel;
		}
		bool flag = status.NetworkLabel == "联网";
		NetworkWiredGlyph.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		NetworkWifiGlyph.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		TextBlock volumeText = VolumeText;
		batteryPercent = status.VolumePercent;
		string text2;
		if (batteryPercent.HasValue)
		{
			int valueOrDefault3 = batteryPercent.GetValueOrDefault();
			text2 = $"{valueOrDefault3}%";
		}
		else
		{
			text2 = "--";
		}
		volumeText.Text = text2;
		VolumeGlyph.Text = (status.IsMuted ? "\ue74f" : "\ue767");
	}

	private async Task RefreshFanAsync(long generation)
	{
		_fanSampling = true;
		try
		{
			int? rpm = await Task.Run((Func<int?>)FanSpeedReader.ReadRpm);
			if (CanApply(generation))
			{
				ApplyFanStatus(rpm);
			}
		}
		catch (Exception)
		{
			if (CanApply(generation))
			{
				ApplyFanStatus(null);
			}
		}
		finally
		{
			_fanSampling = false;
		}
	}

	private void ApplyFanStatus(int? rpm)
	{
		int num;
		if (rpm.HasValue)
		{
			int valueOrDefault = rpm.GetValueOrDefault();
			num = (FanSpeedReader.IsPlausibleRpm(valueOrDefault) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag = (byte)num != 0;
		FanText.Text = (flag ? $"{rpm:N0}" : "--");
		FanPanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint handle = new WindowInteropHelper(this).Handle;
		WindowStyles.MakeNoActivate(handle);
		WindowStyles.SendToDesktopBottom(handle);
		PositionTopRight();
	}

	protected override void OnClosed(EventArgs e)
	{
		_closed = true;
		Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
		_visibilityTimer.Stop();
		StopSampling();
		if (_settings != null)
		{
			_settings.Changed -= OnSettingsChanged;
		}
		_controlCenter.Close();
		_dateNotifications.Close();
		GlassSurface.Dispose();
		base.OnClosed(e);
	}

	private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
	{
		bool locked = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock;
		bool unlocked = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock;
		if (locked || unlocked)
		{
			Dispatcher.BeginInvoke(() =>
			{
				_sessionLocked = locked;
				EvaluateVisibility();
			});
		}
	}
}
