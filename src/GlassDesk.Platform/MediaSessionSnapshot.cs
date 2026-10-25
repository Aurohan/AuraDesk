namespace GlassDesk.Platform;

public readonly record struct MediaSessionSnapshot(bool Available, string Title, string Artist, bool IsPlaying, bool CanGoPrevious, bool CanTogglePlayPause, bool CanGoNext, byte[]? Artwork);
