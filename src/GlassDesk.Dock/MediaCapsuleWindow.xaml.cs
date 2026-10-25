using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GlassDesk.Core.Models.Settings;
using GlassDesk.Platform;
using GlassDesk.Platform.Displays;
using GlassDesk.UI.Controls;

namespace GlassDesk.Dock;

public partial class MediaCapsuleWindow : GlassWindow, IComponentConnector
{
	private const double FullWidth = 378.0;

	private const double FullHeight = 76.0;

	private const double TabWidth = 34.0;

	private const double TabHeight = 54.0;

	private readonly MediaSessionController _controller = new MediaSessionController();

	private readonly DispatcherTimer _timer;

	private PixelRect _anchor;

	private bool _refreshing;

	private bool _running;

	private bool _closed;

	private long _generation;

	private bool _hasMedia;

	private bool _collapsedToTab;

	private bool _modeTransitioning;

	private bool _tabDragging;

	private bool _tabDragged;

	private double _tabDragStartScreenY;

	private double _tabPointerOffsetY;

	private int? _tabTopOverride;

	public MediaCapsuleWindow()
	{
		InitializeComponent();
		PanelCornerRadius = new CornerRadius(38.0);
		GlassSurface.IsBackdropActive = () => IsVisible;
		_timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(2L)
		};
		_timer.Tick += async (object? _, EventArgs _) =>
		{
			await RefreshAsync();
		};
		SetCollapsedToTab(collapsed: true);
	}

	protected override double ResolveCornerRadius(AppearanceSettings appearance)
	{
		return 38.0;
	}

	internal void RequestGlassRefresh()
	{
		if (IsVisible)
		{
			GlassSurface.RequestBackdropRefresh();
		}
	}

	public void Start(PixelRect anchor)
	{
		if (!_closed)
		{
			_running = true;
			_generation++;
			_anchor = anchor;
			GlassSurface.SetPerformanceSleeping(sleeping: false);
			_timer.Start();
			RefreshAsync();
		}
	}

	public void Stop()
	{
		_running = false;
		_generation++;
		_timer.Stop();
		GlassSurface.SetPerformanceSleeping(sleeping: true);
		Hide();
	}

	public void UpdateAnchor(PixelRect anchor)
	{
		if (_anchor.X == anchor.X && _anchor.Y == anchor.Y && _anchor.Width == anchor.Width && _anchor.Height == anchor.Height)
		{
			return;
		}
		if (IsVisible && !_modeTransitioning)
		{
			GlassSurface.BeginRelocationHandoff();
		}
		_anchor = anchor;
		if (IsVisible && !_modeTransitioning)
		{
			PositionForCurrentMode();
			Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
			{
				GlassSurface.RequestBackdropRefresh();
			}));
		}
	}

	private async Task RefreshAsync()
	{
		if (_refreshing || !_running || _closed)
		{
			return;
		}
		_refreshing = true;
		long generation = _generation;
		try
		{
			MediaSessionSnapshot mediaSessionSnapshot = await _controller.ReadAsync();
			if (!_running || _closed || generation != _generation)
			{
				return;
			}
			if (!mediaSessionSnapshot.Available)
			{
				if (!_hasMedia)
				{
					ShowEmptyState();
				}
			}
			else
			{
				_hasMedia = true;
				TitleText.Text = mediaSessionSnapshot.Title;
				ArtistText.Text = mediaSessionSnapshot.Artist;
				PlayPauseGlyph.Text = (mediaSessionSnapshot.IsPlaying ? "\ue769" : "\ue768");
				PreviousButton.IsEnabled = mediaSessionSnapshot.CanGoPrevious;
				PlayPauseButton.IsEnabled = mediaSessionSnapshot.CanTogglePlayPause;
				NextButton.IsEnabled = mediaSessionSnapshot.CanGoNext;
				ApplyArtwork(mediaSessionSnapshot.Artwork);
			}
			if (!IsVisible)
			{
				Show();
			}
			if (!_modeTransitioning)
			{
				PositionForCurrentMode();
			}
		}
		finally
		{
			_refreshing = false;
		}
	}

	private void ShowEmptyState()
	{
		TitleText.Text = "暂无媒体";
		ArtistText.Text = "播放音乐或视频后会自动显示";
		PlayPauseGlyph.Text = "\ue768";
		PreviousButton.IsEnabled = false;
		PlayPauseButton.IsEnabled = false;
		NextButton.IsEnabled = false;
		ApplyArtwork(null);
	}

	private void ApplyArtwork(byte[]? bytes)
	{
		if (bytes == null || bytes.Length == 0)
		{
			ArtworkImage.Source = null;
			ArtworkImage.Visibility = Visibility.Collapsed;
			ArtworkPlaceholder.Visibility = Visibility.Visible;
			return;
		}
		try
		{
			using MemoryStream streamSource = new MemoryStream(bytes, writable: false);
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.StreamSource = streamSource;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			ArtworkImage.Source = bitmapImage;
			ArtworkImage.Visibility = Visibility.Visible;
			ArtworkPlaceholder.Visibility = Visibility.Collapsed;
		}
		catch (NotSupportedException)
		{
			ArtworkImage.Source = null;
			ArtworkImage.Visibility = Visibility.Collapsed;
			ArtworkPlaceholder.Visibility = Visibility.Visible;
		}
	}

	private PixelRect CalculateFullRect()
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null || !primaryDisplaySession.IsAvailable)
		{
			return default;
		}
		DisplayInfo display = primaryDisplaySession.Display;
		int num = display.ToPixels(378.0);
		int num2 = display.ToPixels(76.0);
		int num3 = Math.Max(8, display.ToPixels(10.0));
		int num4 = display.WorkArea.Y + num3;
		int max = Math.Max(num4, display.WorkArea.Bottom - num2 - num3);
		int value = _tabTopOverride ?? (_anchor.Bottom + num3);
		return primaryDisplaySession.ClampWindowToWorkArea(new PixelRect(_anchor.Right - num, Math.Clamp(value, num4, max), num, num2));
	}

	private PixelRect CalculateTabRect()
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null || !primaryDisplaySession.IsAvailable)
		{
			return default;
		}
		DisplayInfo display = primaryDisplaySession.Display;
		int num = display.ToPixels(34.0);
		int num2 = display.ToPixels(54.0);
		int num3 = Math.Max(8, display.ToPixels(10.0));
		int num4 = display.WorkArea.Y + num3;
		int max = Math.Max(num4, display.WorkArea.Bottom - num2 - num3);
		int value = _tabTopOverride ?? Math.Max(num4, _anchor.Bottom + num3);
		return primaryDisplaySession.ClampWindowToWorkArea(new PixelRect(display.WorkArea.Right - num, Math.Clamp(value, num4, max), num, num2));
	}

	private void PositionForCurrentMode()
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable)
		{
			if (!_collapsedToTab)
			{
				PlaceWindowWithinPrimaryWorkArea(CalculateFullRect());
			}
			else
			{
				PlaceWindowWithinPrimaryWorkArea(CalculateTabRect());
			}
		}
	}

	private void SetCollapsedToTab(bool collapsed)
	{
		if (_collapsedToTab != collapsed && !_modeTransitioning)
		{
			_collapsedToTab = collapsed;
			if (!IsLoaded || !IsVisible || Handle == 0)
			{
				ApplyModeImmediately(collapsed);
			}
			else
			{
				AnimateModeChange(collapsed);
			}
		}
	}

	private void ApplyModeImmediately(bool collapsed)
	{
		BeginAnimation(FrameworkElement.WidthProperty, null);
		BeginAnimation(FrameworkElement.HeightProperty, null);
		BeginAnimation(Window.LeftProperty, null);
		BeginAnimation(Window.TopProperty, null);
		Width = (collapsed ? 34.0 : 378.0);
		Height = (collapsed ? 54.0 : 76.0);
		ApplyGlassMaterial(collapsed);
		PanelCornerRadius = new CornerRadius(collapsed ? 17 : 38);
		FullPlayerPanel.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
		SideRevealButton.Visibility = ((!collapsed) ? Visibility.Collapsed : Visibility.Visible);
		PositionForCurrentMode();
		RefreshBackdropAfterModeChange();
	}

	private void AnimateModeChange(bool collapsed)
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable)
		{
			PixelRect? windowPixelBounds = DisplayService.GetWindowPixelBounds(Handle);
			if (windowPixelBounds.HasValue)
			{
				PixelRect valueOrDefault = windowPixelBounds.GetValueOrDefault();
				if (valueOrDefault.Width > 0 && valueOrDefault.Height > 0)
				{
					_modeTransitioning = true;
					DisplayInfo display = primaryDisplaySession.Display;
					PixelRect target = (collapsed ? CalculateTabRect() : CalculateFullRect());
					TimeSpan duration = TimeSpan.FromMilliseconds(180L);
					BeginAnimation(FrameworkElement.WidthProperty, null);
					BeginAnimation(FrameworkElement.HeightProperty, null);
					BeginAnimation(Window.LeftProperty, null);
					BeginAnimation(Window.TopProperty, null);
					Width = display.ToDips(valueOrDefault.Width);
					Height = display.ToDips(valueOrDefault.Height);
					Left = display.ToDips(valueOrDefault.X);
					Top = display.ToDips(valueOrDefault.Y);
					if (!collapsed)
					{
						PanelCornerRadius = new CornerRadius(38.0);
						FullPlayerPanel.Visibility = Visibility.Visible;
						SideRevealButton.Visibility = Visibility.Collapsed;
					}
					DoubleAnimation doubleAnimation = CreateModeAnimation(Width, display.ToDips(target.Width), duration);
					doubleAnimation.Completed += (object? _, EventArgs _) =>
					{
						BeginAnimation(FrameworkElement.WidthProperty, null);
						BeginAnimation(FrameworkElement.HeightProperty, null);
						BeginAnimation(Window.LeftProperty, null);
						BeginAnimation(Window.TopProperty, null);
						Width = display.ToDips(target.Width);
						Height = display.ToDips(target.Height);
						Left = display.ToDips(target.X);
						Top = display.ToDips(target.Y);
						ApplyGlassMaterial(collapsed);
						PanelCornerRadius = new CornerRadius(collapsed ? 17 : 38);
						FullPlayerPanel.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
						SideRevealButton.Visibility = ((!collapsed) ? Visibility.Collapsed : Visibility.Visible);
						_modeTransitioning = false;
						PositionForCurrentMode();
						RefreshBackdropAfterModeChange();
					};
					BeginAnimation(FrameworkElement.WidthProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
					BeginAnimation(FrameworkElement.HeightProperty, CreateModeAnimation(Height, display.ToDips(target.Height), duration), HandoffBehavior.SnapshotAndReplace);
					BeginAnimation(Window.LeftProperty, CreateModeAnimation(Left, display.ToDips(target.X), duration), HandoffBehavior.SnapshotAndReplace);
					BeginAnimation(Window.TopProperty, CreateModeAnimation(Top, display.ToDips(target.Y), duration), HandoffBehavior.SnapshotAndReplace);
					return;
				}
			}
		}
		ApplyModeImmediately(collapsed);
	}

	private static DoubleAnimation CreateModeAnimation(double from, double to, TimeSpan duration)
	{
		return new DoubleAnimation(from, to, duration)
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseInOut
			},
			FillBehavior = FillBehavior.HoldEnd
		};
	}

	private void ApplyGlassMaterial(bool collapsed)
	{
		GlassSurface.UseFrostedMaterial = !collapsed;
		GlassSurface.BackgroundClarityOverride = (collapsed ? double.NaN : 0.35);
	}

	private void RefreshBackdropAfterModeChange()
	{
		Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
		{
			if (IsVisible)
			{
				GlassSurface.RequestBackdropRefresh(revealing: true);
			}
		}));
	}

	private void OnManualCollapseClick(object sender, RoutedEventArgs e)
	{
		SetCollapsedToTab(collapsed: true);
		if (!IsVisible)
		{
			Show();
		}
		PositionForCurrentMode();
	}

	private void OnSideRevealPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (!_modeTransitioning)
		{
			PixelRect? windowPixelBounds = DisplayService.GetWindowPixelBounds(Handle);
			if (windowPixelBounds.HasValue)
			{
				PixelRect valueOrDefault = windowPixelBounds.GetValueOrDefault();
				Point point = PointToScreen(e.GetPosition(this));
				_tabDragging = true;
				_tabDragged = false;
				_tabDragStartScreenY = point.Y;
				_tabPointerOffsetY = point.Y - (double)valueOrDefault.Y;
				SideRevealButton.CaptureMouse();
			}
		}
	}

	private void OnSideRevealPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (_tabDragging && e.LeftButton == MouseButtonState.Pressed)
		{
			Point point = PointToScreen(e.GetPosition(this));
			if (_tabDragged || !(Math.Abs(point.Y - _tabDragStartScreenY) < 4.0))
			{
				_tabDragged = true;
				MoveCollapsedTabTo(point.Y - _tabPointerOffsetY);
			}
		}
	}

	private void OnSideRevealPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (!_tabDragging)
		{
			return;
		}
		_tabDragging = false;
		if (SideRevealButton.IsMouseCaptured)
		{
			SideRevealButton.ReleaseMouseCapture();
		}
		if (_tabDragged)
		{
			_tabDragged = false;
			Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
			{
				GlassSurface.RequestBackdropRefresh();
			}));
		}
		else
		{
			SetCollapsedToTab(collapsed: false);
			if (!IsVisible)
			{
				Show();
			}
			PositionForCurrentMode();
		}
		e.Handled = true;
	}

	private void MoveCollapsedTabTo(double desiredScreenY)
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable)
		{
			DisplayInfo display = primaryDisplaySession.Display;
			int num = display.ToPixels(34.0);
			int num2 = display.ToPixels(54.0);
			int num3 = Math.Max(8, display.ToPixels(10.0));
			int num4 = display.WorkArea.Y + num3;
			int num5 = Math.Max(num4, display.WorkArea.Bottom - num2 - num3);
			_tabTopOverride = (int)Math.Round(Math.Clamp(desiredScreenY, num4, num5));
			PlaceWindowWithinPrimaryWorkArea(new PixelRect(display.WorkArea.Right - num, _tabTopOverride.Value, num, num2));
		}
	}

	private async void OnPreviousClick(object sender, RoutedEventArgs e)
	{
		await _controller.PreviousAsync();
		await RefreshAsync();
	}

	private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
	{
		await _controller.TogglePlayPauseAsync();
		await RefreshAsync();
	}

	private async void OnNextClick(object sender, RoutedEventArgs e)
	{
		await _controller.NextAsync();
		await RefreshAsync();
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		WindowStyles.MakeNoActivate(new WindowInteropHelper(this).Handle);
	}

	protected override void OnClosed(EventArgs e)
	{
		_closed = true;
		_running = false;
		_generation++;
		_timer.Stop();
		_controller.Dispose();
		base.OnClosed(e);
	}
}
