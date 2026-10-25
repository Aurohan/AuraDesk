namespace GlassDesk.Platform;

public readonly record struct ActiveAudioApplicationSnapshot(
    bool Available, 
    string Title, 
    string ApplicationName, 
    int ProcessId, 
    int VolumePercent = 100, 
    bool IsMuted = false);
