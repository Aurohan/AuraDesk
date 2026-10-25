using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace GlassDesk.Platform;

public sealed class MediaSessionController : IDisposable
{
	private GlobalSystemMediaTransportControlsSessionManager? _manager;

	private bool _disposed;

	private bool _usingAudioFallback;

	private MediaSessionSnapshot? _lastSnapshot;

	private string? _lastSourceAppUserModelId;

	public string LastDiagnostic { get; private set; } = "尚未读取媒体会话";

	public async Task<MediaSessionSnapshot> ReadAsync()
	{
		_ = 2;
		try
		{
			GlobalSystemMediaTransportControlsSession session = await GetCurrentSessionAsync();
			if ((object)session == null)
			{
				return ReadAudioFallback();
			}
			_usingAudioFallback = false;
			GlobalSystemMediaTransportControlsSessionMediaProperties properties = await session.TryGetMediaPropertiesAsync();
			GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = session.GetPlaybackInfo();
			GlobalSystemMediaTransportControlsSessionPlaybackControls controls = playback.Controls;
			byte[] artwork = await ReadArtworkAsync(properties.Thumbnail);
			LastDiagnostic = "已读取媒体会话：" + session.SourceAppUserModelId;
			MediaSessionSnapshot mediaSessionSnapshot = new MediaSessionSnapshot(Available: true, string.IsNullOrWhiteSpace(properties.Title) ? "正在播放" : properties.Title, string.IsNullOrWhiteSpace(properties.Artist) ? session.SourceAppUserModelId : properties.Artist, playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, controls.IsPreviousEnabled, controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled, controls.IsNextEnabled, artwork);
			_lastSnapshot = mediaSessionSnapshot;
			_lastSourceAppUserModelId = session.SourceAppUserModelId;
			return mediaSessionSnapshot;
		}
		catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is PlatformNotSupportedException || ex is TypeLoadException || ex is COMException) ? true : false)
		{
			LastDiagnostic = $"{ex.GetType().Name} (0x{ex.HResult:X8})：{ex.Message}";
			return ReadAudioFallback();
		}
	}

	private MediaSessionSnapshot ReadAudioFallback()
	{
		ActiveAudioApplicationSnapshot activeAudioApplicationSnapshot = ActiveAudioApplicationReader.Read();
		if (!activeAudioApplicationSnapshot.Available)
		{
			_usingAudioFallback = false;
			MediaSessionSnapshot? lastSnapshot = _lastSnapshot;
			if (lastSnapshot.HasValue)
			{
				MediaSessionSnapshot valueOrDefault = lastSnapshot.GetValueOrDefault();
				if (valueOrDefault.Available)
				{
					MediaSessionSnapshot mediaSessionSnapshot = valueOrDefault with
					{
						IsPlaying = false,
						CanTogglePlayPause = true
					};
					_lastSnapshot = mediaSessionSnapshot;
					LastDiagnostic = (((object)_manager == null) ? "媒体会话管理器暂不可用；已保留上次媒体，可用系统媒体键恢复" : "Windows 暂未返回媒体会话；已保留上次媒体，可用系统媒体键恢复");
					return mediaSessionSnapshot;
				}
			}
			LastDiagnostic = (((object)_manager == null) ? "媒体会话管理器不可用，且没有活动音频应用" : "Windows 未返回媒体会话，且没有活动音频应用");
			return default;
		}
		_usingAudioFallback = true;
		LastDiagnostic = $"兼容模式：正在发声的应用为 {activeAudioApplicationSnapshot.ApplicationName} ({activeAudioApplicationSnapshot.ProcessId})";
		MediaSessionSnapshot mediaSessionSnapshot2 = new MediaSessionSnapshot(Available: true, activeAudioApplicationSnapshot.Title, activeAudioApplicationSnapshot.ApplicationName + " · 兼容模式", IsPlaying: true, CanGoPrevious: true, CanTogglePlayPause: true, CanGoNext: true, null);
		_lastSnapshot = mediaSessionSnapshot2;
		_lastSourceAppUserModelId = null;
		return mediaSessionSnapshot2;
	}

	private static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? reference)
	{
		if (reference == null)
		{
			return null;
		}
		try
		{
			using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
			ulong size = stream.Size;
			if ((size > 4194304 || size == 0L) ? true : false)
			{
				return null;
			}
			uint count = checked((uint)stream.Size);
			using DataReader reader = new DataReader(stream.GetInputStreamAt(0uL));
			uint num = await reader.LoadAsync(count);
			if (num == 0)
			{
				return null;
			}
			byte[] array = new byte[num];
			reader.ReadBytes(array);
			return array;
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is OverflowException || ex is COMException) ? true : false)
		{
			return null;
		}
	}

	public Task<bool> PreviousAsync()
	{
		return InvokeAsync((GlobalSystemMediaTransportControlsSession session) => session.TrySkipPreviousAsync(), MediaKeyCommand.Previous);
	}

	public Task<bool> TogglePlayPauseAsync()
	{
		return InvokeAsync((GlobalSystemMediaTransportControlsSession session) => session.TryTogglePlayPauseAsync(), MediaKeyCommand.PlayPause);
	}

	public Task<bool> NextAsync()
	{
		return InvokeAsync((GlobalSystemMediaTransportControlsSession session) => session.TrySkipNextAsync(), MediaKeyCommand.Next);
	}

	private async Task<GlobalSystemMediaTransportControlsSession?> GetCurrentSessionAsync()
	{
		if (_disposed)
		{
			return null;
		}
		if ((object)_manager == null)
		{
			_manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
		}
		GlobalSystemMediaTransportControlsSession globalSystemMediaTransportControlsSession = _manager.GetCurrentSession();
		if ((object)globalSystemMediaTransportControlsSession != null && MediaSourceFilter.IsExcluded(globalSystemMediaTransportControlsSession.SourceAppUserModelId))
		{
			globalSystemMediaTransportControlsSession = null;
		}
		GlobalSystemMediaTransportControlsSession[] array = (from session in _manager.GetSessions()
			where !MediaSourceFilter.IsExcluded(session.SourceAppUserModelId)
			select session).ToArray();
		GlobalSystemMediaTransportControlsSession[] array2 = array;
		foreach (GlobalSystemMediaTransportControlsSession globalSystemMediaTransportControlsSession2 in array2)
		{
			try
			{
				if (globalSystemMediaTransportControlsSession2.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
				{
					return globalSystemMediaTransportControlsSession2;
				}
			}
			catch (COMException)
			{
			}
		}
		if (!string.IsNullOrWhiteSpace(_lastSourceAppUserModelId))
		{
			array2 = array;
			foreach (GlobalSystemMediaTransportControlsSession globalSystemMediaTransportControlsSession3 in array2)
			{
				if (string.Equals(globalSystemMediaTransportControlsSession3.SourceAppUserModelId, _lastSourceAppUserModelId, StringComparison.OrdinalIgnoreCase))
				{
					return globalSystemMediaTransportControlsSession3;
				}
			}
		}
		return globalSystemMediaTransportControlsSession ?? array.FirstOrDefault();
	}

	private async Task<bool> InvokeAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action, MediaKeyCommand fallbackCommand)
	{
		_ = 1;
		try
		{
			GlobalSystemMediaTransportControlsSession globalSystemMediaTransportControlsSession = await GetCurrentSessionAsync();
			if ((object)globalSystemMediaTransportControlsSession != null)
			{
				_usingAudioFallback = false;
				if (await action(globalSystemMediaTransportControlsSession))
				{
					return true;
				}
			}
			bool flag = _lastSnapshot?.Available ?? false;
			bool num = (_usingAudioFallback | flag) && MediaKeySender.Send(fallbackCommand);
			if (num && fallbackCommand == MediaKeyCommand.PlayPause)
			{
				MediaSessionSnapshot? lastSnapshot = _lastSnapshot;
				if (lastSnapshot.HasValue)
				{
					MediaSessionSnapshot valueOrDefault = lastSnapshot.GetValueOrDefault();
					_lastSnapshot = valueOrDefault with
					{
						IsPlaying = !valueOrDefault.IsPlaying,
						CanTogglePlayPause = true
					};
				}
			}
			return num;
		}
		catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is PlatformNotSupportedException || ex is TypeLoadException || ex is COMException) ? true : false)
		{
			return false;
		}
	}

	public void Dispose()
	{
		_disposed = true;
		_manager = null;
		_lastSnapshot = null;
		_lastSourceAppUserModelId = null;
	}
}
