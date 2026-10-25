namespace GlassDesk.Platform;

public readonly record struct SystemPerformanceSnapshot(
    int? CpuPercent, 
    int? MemoryPercent, 
    string DownSpeedText = "", 
    string UpSpeedText = "",
    double DownBytesPerSec = 0,
    double UpBytesPerSec = 0);
