namespace GlassDesk.Platform;

public readonly record struct SystemStatusSnapshot(int? BatteryPercent, bool IsCharging, string NetworkLabel, int? VolumePercent, bool IsMuted)
{
	public bool HasBattery { get; init; } = true;
}
