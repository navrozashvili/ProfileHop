using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProfileHop.Core.Apps;
using ProfileHop.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;

namespace ProfileHop.Tray;

/// <summary>
/// Reports which app is in front, via an out-of-context <c>EVENT_SYSTEM_FOREGROUND</c> WinEvent hook: nothing is
/// injected into other processes, and they are only opened with <c>PROCESS_QUERY_LIMITED_INFORMATION</c>. Must be
/// created on the UI thread (the one running GetMessage); <c>appChanged</c> is raised there.
/// </summary>
internal sealed unsafe class ForegroundWatcher : IDisposable
{
    private const int MaxPath = 1024;      // stack buffer for image paths (longer paths are reported as unresolvable)
    private const int MaxClass = 256;      // window class names are at most 256 chars

    private static ForegroundWatcher? s_watcher;

    private readonly Action<string> _appChanged;
    private readonly ILog _log;
    private HWINEVENTHOOK _hook;

    public ForegroundWatcher(Action<string> appChanged, ILog log)
    {
        if (s_watcher is not null) throw new InvalidOperationException("Only one ForegroundWatcher may exist.");
        _appChanged = appChanged;
        _log = log;
        s_watcher = this;
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, default(HMODULE),
            &OnWinEvent, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
        if (_hook.IsNull) _log.Write(LogLevel.Warning, "SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed; per-app profiles are off.");
    }

    /// <summary>The app key of the window in front now, or null if it is a shell surface or can't be identified.</summary>
    public string? Current() => Resolve(PInvoke.GetForegroundWindow());

    public void Dispose()
    {
        if (!_hook.IsNull)
        {
            PInvoke.UnhookWinEvent(_hook);
            _hook = default;
        }
        if (s_watcher == this) s_watcher = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(HWINEVENTHOOK hook, uint evt, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var watcher = s_watcher;
        if (watcher is null || watcher._hook != hook) return;
        try
        {
            if (watcher.Resolve(hwnd) is { } key) watcher._appChanged(key);
        }
        catch (Exception ex)
        {
            watcher._log.Write(LogLevel.Error, $"Foreground event failed: {ex}");
        }
    }

    /// <summary>window → pid → image path (frame hosts: the hosted app's process) → app key; null when skipped.</summary>
    private string? Resolve(HWND hwnd)
    {
        if (hwnd.IsNull) return null;

        Span<char> classBuf = stackalloc char[MaxClass];
        int classLen;
        fixed (char* p = classBuf) classLen = Math.Max(0, PInvoke.GetClassName(hwnd, p, MaxClass));
        var className = classLen > 0 ? classBuf[..classLen].ToString() : null;

        uint pid;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        Span<char> path = stackalloc char[MaxPath];
        var len = ImagePath(pid, path);
        if (len == 0)
        {
            if (_log.IsEnabled(LogLevel.Debug)) _log.Write(LogLevel.Debug, $"Foreground window ({className}, pid {pid}) not identifiable; ignored.");
            return null;
        }

        var exe = Path.GetFileName(path[..len]).ToString();
        if (AppRules.IsFrameHost(exe) && HostedProcess(hwnd, pid) is var child && child != 0)
        {
            Span<char> childPath = stackalloc char[MaxPath];
            var childLen = ImagePath(child, childPath);
            if (childLen > 0)
            {
                childPath[..childLen].CopyTo(path);
                len = childLen;
                exe = Path.GetFileName(path[..len]).ToString();
            }
        }

        if (AppRules.IsShellSurface(className, exe))
        {
            if (_log.IsEnabled(LogLevel.Debug)) _log.Write(LogLevel.Debug, $"Foreground shell surface ({className}, {exe}); ignored.");
            return null;
        }

        var imagePath = path[..len].ToString();
        if (_log.IsEnabled(LogLevel.Debug)) _log.Write(LogLevel.Debug, $"Foreground app ({className}): {imagePath}");
        return AppRules.Key(imagePath);
    }

    /// <summary>Writes the process's Win32 image path into <paramref name="buffer"/>; returns its length, 0 on failure.</summary>
    private static int ImagePath(uint pid, Span<char> buffer)
    {
        if (pid == 0) return 0;
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsNull) return 0;
        try
        {
            var size = (uint)buffer.Length;
            fixed (char* p = buffer)
            {
                return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &size)
                    ? (int)size : 0;
            }
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    private struct ChildSearch
    {
        public uint FramePid;
        public uint Found;
    }

    /// <summary>The pid of the first child window owned by another process than the frame host; 0 if none.</summary>
    private static uint HostedProcess(HWND frame, uint framePid)
    {
        var search = new ChildSearch { FramePid = framePid };
        PInvoke.EnumChildWindows(frame, &FindHostedChild, (LPARAM)(nint)(&search));
        return search.Found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL FindHostedChild(HWND child, LPARAM lParam)
    {
        var search = (ChildSearch*)lParam.Value;
        uint pid;
        PInvoke.GetWindowThreadProcessId(child, &pid);
        if (pid == 0 || pid == search->FramePid) return true; // keep looking
        search->Found = pid;
        return false;
    }
}
