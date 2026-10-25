using System;

namespace GlassDesk.Platform;

public sealed record WifiNetworkOption(Guid InterfaceId, string Name, string ProfileName, int SignalQuality, bool IsConnected, bool HasSavedProfile);
