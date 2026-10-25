using System.Runtime.CompilerServices;

namespace GlassDesk.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 命令行救急恢复：一键复原系统任务栏与桌面图标
        if (args.Any(a => string.Equals(a, "--restore-shell", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-restore-shell", StringComparison.OrdinalIgnoreCase)))
        {
            GlassDesk.Platform.Shell.ShellEmergencyRescue.RestoreShell();
            return 0;
        }

        // Capture helpers own native resources only. Loading the desktop Application
        // and its theme dictionaries before the pipe handshake delays every cold reveal.
        if (UI.BackdropCaptureWorker.RunIfRequested(args, out var exitCode)) return exitCode;
        return RunDesktop();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunDesktop()
    {
        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }
}
