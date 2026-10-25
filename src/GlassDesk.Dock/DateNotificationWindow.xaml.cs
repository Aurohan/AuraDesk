using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using GlassDesk.Core.Models.Settings;
using GlassDesk.Platform.Displays;
using GlassDesk.Platform.Shell;
using GlassDesk.UI.Controls;

namespace GlassDesk.Dock;

public partial class DateNotificationWindow : GlassWindow, IComponentConnector
{
	private readonly DispatcherTimer _timer;

	private readonly PopupRevealAnimation _reveal;

	public DateNotificationWindow()
	{
		InitializeComponent();
		_reveal = new PopupRevealAnimation(this, PopupContent);
		PanelCornerRadius = new CornerRadius(32.0);
		GlassSurface.IsBackdropActive = () => IsVisible;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_timer.Tick += (object? _, EventArgs _) =>
		{
			RefreshClock();
		};
		IsVisibleChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			if (IsVisible)
			{
				RefreshClock();
				MonthCalendar.DisplayDate = DateTime.Today;
				_timer.Start();
			}
			else
			{
				_timer.Stop();
			}
		};
		Deactivated += (object? _, EventArgs _) =>
		{
			Hide();
		};
		PreviewKeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Escape)
			{
				Hide();
			}
		};
	}

	protected override double ResolveCornerRadius(AppearanceSettings appearance)
	{
		return 32.0;
	}

	internal void RequestGlassRefresh()
	{
		if (IsVisible)
		{
			GlassSurface.RequestBackdropRefresh();
		}
	}

	public void ShowNear(PixelRect anchor)
	{
		if (!IsVisible)
		{
			new WindowInteropHelper(this).EnsureHandle();
			PixelRect pixelRect = PositionNearAnchor(anchor);
			PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
			if (primaryDisplaySession != null && primaryDisplaySession.IsAvailable && pixelRect.Width > 0 && pixelRect.Height > 0)
			{
				GlassSurface.PrepareRevealMaterial(pixelRect, pixelRect, primaryDisplaySession.Display.Dpi);
			}
			_reveal.Prepare();
			Show();
		}
		PositionNearAnchor(anchor);
		Activate();
	}

	internal PixelRect CalculatePlacement(PixelRect anchor)
	{
		PrimaryDisplaySession primaryDisplaySession = PrimaryDisplaySession;
		if (primaryDisplaySession == null || !primaryDisplaySession.IsAvailable)
		{
			return default;
		}
		DisplayInfo display = primaryDisplaySession.Display;
		int num = display.ToPixels(Width);
		int height = display.ToPixels(Height);
		int num2 = Math.Max(8, display.ToPixels(10.0));
		return primaryDisplaySession.ClampWindowToWorkArea(new PixelRect(anchor.Right - num, anchor.Bottom + num2, num, height));
	}

	private PixelRect PositionNearAnchor(PixelRect anchor)
	{
		return PlaceWindowWithinPrimaryWorkArea(CalculatePlacement(anchor));
	}

	private void RefreshClock()
	{
		DateTime now = DateTime.Now;
		LargeTimeText.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
		LargeDateText.Text = now.ToString("yyyy年M月d日 dddd", CultureInfo.CurrentCulture);
	}

	private void OnNotificationSettingsClick(object sender, RoutedEventArgs e)
	{
		ShellExecutor.Open("ms-settings:notifications");
	}

	protected override void OnClosed(EventArgs e)
	{
		_timer.Stop();
		base.OnClosed(e);
	}
	private StickyNotesWindow? _stickyNotes;

	private void OnOpenStickyNotesClick(object sender, RoutedEventArgs e)
	{
		Hide();
		_stickyNotes ??= new StickyNotesWindow();
		if (_stickyNotes.IsVisible)
		{
			_stickyNotes.Activate();
		}
		else
		{
			_stickyNotes.Show();
		}
	}

}