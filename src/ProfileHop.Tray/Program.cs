using System.Runtime.InteropServices;
using ProfileHop.Core;
using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ProfileHop.Tray;

internal static class Program
{
    private const uint ErrorAlreadyExists = 183;

    /// <summary>
    /// The one seam the orchestrator wires after merging the mouse side:
    /// <c>return new MouseController(new WindowsHidDeviceSource());</c> (pass <paramref name="config"/>.ForceOnboardMode /
    /// .Device and <paramref name="log"/> if the controller accepts them).
    /// </summary>
    internal static IProfileSwitcher CreateSwitcher(AppConfig config, ILog log) =>
        new MouseController(new ProfileHop.Core.Hid.WindowsHidDeviceSource());

    /// <summary>
    /// Usage: <c>profilehop [--no-mouse] [--data-dir &lt;folder&gt;]</c>. <c>--no-mouse</c> runs the UI with a switcher
    /// that always fails (grey icon), for testing without hardware. <c>--data-dir</c> replaces %APPDATA%\ProfileHop.
    /// </summary>
    [STAThread]
    private static unsafe int Main(string[] args)
    {
        var noMouse = args.Contains("--no-mouse", StringComparer.OrdinalIgnoreCase);
        var dataDir = ArgValue(args, "--data-dir") ?? ConfigStore.DefaultDirectory;

        HANDLE mutex;
        fixed (char* name = @"Local\ProfileHop")
        {
            mutex = PInvoke.CreateMutex(null, false, name);
        }
        if (mutex.IsNull || (uint)Marshal.GetLastPInvokeError() == ErrorAlreadyExists)
        {
            if (!mutex.IsNull) PInvoke.CloseHandle(mutex);
            return 0; // already running
        }

        try
        {
            var configPath = Path.Combine(dataDir, "config.json");
            var loaded = ConfigStore.Load(configPath);
            using var log = new RollingLog(Path.Combine(dataDir, "logs"), loaded.Config.LogLevel);
            log.Write(LogLevel.Info, $"ProfileHop starting (pid {Environment.ProcessId}){(noMouse ? ", --no-mouse" : "")}.");
            foreach (var e in loaded.Errors) log.Write(LogLevel.Warning, e);

            using var switcher = noMouse ? new NoMouseSwitcher() : CreateSwitcher(loaded.Config, log);
            using var worker = new ProfileWorker(switcher, log) { CycleOverride = loaded.Config.Cycle };
            var autostart = new Autostart(new RegistryRunKeyStore(), Environment.ProcessPath ?? "profilehop.exe");

            using var app = new TrayApp(configPath, loaded, log, worker, autostart);
            var code = app.Run();
            log.Write(LogLevel.Info, "ProfileHop exiting.");
            return code;
        }
        finally
        {
            PInvoke.CloseHandle(mutex);
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Stand-in for <c>--no-mouse</c>: every call fails, so the tray shows "Mouse not reachable".</summary>
    private sealed class NoMouseSwitcher : IProfileSwitcher
    {
        public event Action<int>? ProfileChangedByDevice
        {
            add { }
            remove { }
        }

        public ProfileState GetState() => throw new InvalidOperationException("Started with --no-mouse.");

        public ProfileState SetProfile(int profile) => throw new InvalidOperationException("Started with --no-mouse.");

        public int NextDpiSlot() => throw new InvalidOperationException("Started with --no-mouse.");

        public void MarkStale() { }

        public void Dispose() { }
    }
}
