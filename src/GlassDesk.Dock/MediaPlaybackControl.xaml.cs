using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GlassDesk.Platform;

namespace GlassDesk.Dock;

public partial class MediaPlaybackControl : UserControl, IDisposable, IComponentConnector
{
	private readonly MediaSessionController _controller = new MediaSessionController();

	private readonly DispatcherTimer _timer;

	private bool _running;

	private bool _refreshing;

	private bool _closed;

	private long _generation;

	public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

	public MediaPlaybackControl()
	{
		InitializeComponent();
		_timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(2L)
		};
		_timer.Tick += async (object? _, EventArgs _) =>
		{
			await RefreshAsync();
		};
	}

	public void Start()
	{
		if (!_running && !_closed)
		{
			_running = true;
			_generation++;
			_timer.Start();
			RefreshAsync();
		}
	}

	public void Stop()
	{
		_running = false;
		_generation++;
		_timer.Stop();
	}

	private async Task RefreshAsync()
	{
		if (!_running || _closed || _refreshing)
		{
			return;
		}
		_refreshing = true;
		long generation = _generation;
		try
		{
			MediaSessionSnapshot snapshot = await _controller.ReadAsync();
			if (_closed || Dispatcher.HasShutdownStarted)
			{
				return;
			}
			await Dispatcher.InvokeAsync(() =>
			{
				if (_running && !_closed && generation == _generation)
				{
					ApplySnapshot(snapshot);
				}
			});
		}
		finally
		{
			_refreshing = false;
		}
	}

	public void ApplySnapshot(MediaSessionSnapshot snapshot)
	{
		TitleText.Text = (snapshot.Available ? snapshot.Title : "暂无媒体");
		ArtistText.Text = (snapshot.Available ? snapshot.Artist : "播放音乐或视频后显示");
		PlayPauseGlyph.Text = (snapshot.IsPlaying ? "\ue769" : "\ue768");
		PreviousButton.IsEnabled = snapshot.Available && snapshot.CanGoPrevious;
		PlayPauseButton.IsEnabled = snapshot.Available && snapshot.CanTogglePlayPause;
		NextButton.IsEnabled = snapshot.Available && snapshot.CanGoNext;
		BitmapImage bitmapImage = null;
		byte[] artwork = snapshot.Artwork;
		if (artwork != null && artwork.Length > 0)
		{
			try
			{
				using MemoryStream streamSource = new MemoryStream(artwork, writable: false);
				bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.StreamSource = streamSource;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
			}
			catch (Exception ex) when ((ex is NotSupportedException || ex is IOException || ex is ArgumentException) ? true : false)
			{
				bitmapImage = null;
			}
		}
		ArtworkImage.Source = bitmapImage;
		ArtworkImage.Visibility = ((bitmapImage == null) ? Visibility.Collapsed : Visibility.Visible);
		ArtworkPlaceholder.Visibility = ((bitmapImage != null) ? Visibility.Collapsed : Visibility.Visible);
		SnapshotChanged?.Invoke(this, snapshot);
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

	public void Dispose()
	{
		if (!_closed)
		{
			_closed = true;
			Stop();
			_controller.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
