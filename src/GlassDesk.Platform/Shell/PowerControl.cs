using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GlassDesk.Platform.Shell;

/// <summary>
/// 系统核心电源控制（锁屏、睡眠、重启、关机）。
/// </summary>
public static class PowerControl
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static bool Lock()
    {
        try
        {
            return LockWorkStation();
        }
        catch
        {
            return false;
        }
    }

    public static bool Sleep()
    {
        try
        {
            return SetSuspendState(false, false, false);
        }
        catch
        {
            return false;
        }
    }

    public static void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
        }
    }

    public static void Shutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/s /t 0",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
        }
    }
}
