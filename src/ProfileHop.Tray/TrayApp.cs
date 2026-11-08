using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ProfileHop.Tray;

/// <summary>
/// The tray shell: one hidden window on the UI thread that owns the hotkeys, device notifications, tray icon and
/// menu. All mouse I/O goes through <see cref="ProfileWorker"/>; the UI thread only sits in GetMessage.
/// </summary>
internal sealed unsafe class TrayApp : IDisposable, IHotkeyDialogHost
{
    private const uint WmTray = PInvoke.WM_APP + 1;
    private const uint WmStatus = PInvoke.WM_APP + 2;
    private const uint WmDevices = PInvoke.WM_APP + 3;
    private const uint NinKeySelect = PInvoke.NIN_SELECT | 0x1; // NINF_KEY
    private const uint ModNoRepeat = 0x4000;
    private const uint DbtDevtypDeviceInterface = 5;
    private const string ClassName = "ProfileHopTray";

    // GUID_DEVINTERFACE_HID
    private static readonly Guid HidInterfaceGuid = new(0x4D1E55B2, 0xF16F, 0x11CF, 0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30);

    private const int CmdReload = 1, CmdOpenFolder = 2, CmdAutostart = 3, CmdExit = 4, CmdHotkeys = 5,
        CmdDeviceAuto = 6, CmdRescan = 7, CmdDeviceBase = 500, CmdProfileBase = 1000;

    private static TrayApp? s_app;

    private readonly string _configPath;
    private readonly RollingLog _log;
    private readonly ProfileWorker _worker;
    private readonly Autostart _autostart;
    private readonly uint _taskbarCreated;
    private AppConfig _config;
    private HotkeyPlan _plan = new([], []);
    private readonly List<int> _registered = [];
    private HWND _hwnd;
    private HDEVNOTIFY _devNotify;
    private HICON _icon;
    private (string Glyph, bool Active) _iconKey;
    private WorkerStatus _shown = WorkerStatus.Starting;
    private ProfileHop.Core.ProfileState? _lastState;   // last state the mouse reported (kept while unreachable): profile list and names
    private bool _iconAdded;
    private bool _exiting;
    private HMENU _deviceMenu;          // Device submenu while the context menu is open; filled on WM_INITMENUPOPUP
    private bool _deviceMenuFilled;
    private IReadOnlyList<DeviceMenuItem> _deviceItems = [];
    private bool _rescanPending;

    public TrayApp(string configPath, ConfigLoadResult loaded, RollingLog log, ProfileWorker worker, Autostart autostart)
    {
        _configPath = configPath;
        _config = loaded.Config;
        _log = log;
        _worker = worker;
        _autostart = autostart;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

        s_app = this;
        CreateWindow();
        AddTrayIcon();
        RegisterDeviceNotifications();

        var problems = new List<string>(loaded.Errors);
        problems.AddRange(RegisterHotkeys());
        if (problems.Count > 0) ShowBalloon("ProfileHop", string.Join("\n", problems), warning: true);

        try
        {
            if (_autostart.RepairPath()) _log.Write(LogLevel.Info, "Autostart path updated to this exe.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Write(LogLevel.Warning, $"Autostart check failed: {ex.Message}");
        }

        _worker.StatusChanged += OnWorkerStatus;
        _worker.DevicesChanged += OnWorkerDevices;
        _worker.Start();
        _worker.RequestRefresh(); // connect in the background and show the current profile
    }

    public int Run()
    {
        MSG msg;
        while (PInvoke.GetMessage(&msg, default, 0, 0).Value > 0)
        {
            if (HotkeyDialog.Current is { } dialog && PInvoke.IsDialogMessage(dialog.Hwnd, &msg)) continue;
            PInvoke.TranslateMessage(&msg);
            PInvoke.DispatchMessage(&msg);
        }
        return (int)msg.wParam.Value;
    }

    public void Dispose()
    {
        _worker.StatusChanged -= OnWorkerStatus;
        _worker.DevicesChanged -= OnWorkerDevices;
        if (!_hwnd.IsNull) PInvoke.DestroyWindow(_hwnd);
        s_app = null;
    }

    // ---- Window ----

    private void CreateWindow()
    {
        var hinstance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = hinstance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(&wc) == 0) throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");

            // A hidden top-level tool window rather than HWND_MESSAGE: message-only windows miss the TaskbarCreated and
            // WM_POWERBROADCAST broadcasts and can't take foreground for the popup menu. It is never shown.
            _hwnd = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, className, WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0, default, default, hinstance, null);
        }
        if (_hwnd.IsNull) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        var app = s_app;
        if (app is not null && app._hwnd == hwnd)
        {
            try
            {
                if (app.Handle(msg, wParam, lParam) is LRESULT handled) return handled;
            }
            catch (Exception ex)
            {
                app._log.Write(LogLevel.Error, $"Window message 0x{msg:X} failed: {ex}");
            }
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private LRESULT? Handle(uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_HOTKEY:
                OnHotkey((int)wParam.Value);
                return new LRESULT(0);

            case PInvoke.WM_COMMAND: // menu ids also work as posted commands (scripting/tests)
                Execute((int)(wParam.Value & 0xFFFF));
                return new LRESULT(0);

            case WmStatus:
                ShowStatus(_worker.Status);
                return new LRESULT(0);

            case WmDevices:
                OnDevicesListed();
                return new LRESULT(0);

            case PInvoke.WM_INITMENUPOPUP:
                if (!_deviceMenu.IsNull && (nint)wParam.Value == (nint)_deviceMenu.Value && !_deviceMenuFilled) FillDeviceMenu();
                return new LRESULT(0);

            case WmTray:
                var evt = (uint)(lParam.Value & 0xFFFF);
                if (evt is PInvoke.WM_CONTEXTMENU or PInvoke.NIN_SELECT or NinKeySelect)
                {
                    var x = (short)(wParam.Value & 0xFFFF);
                    var y = (short)((wParam.Value >> 16) & 0xFFFF);
                    ShowMenu(x, y);
                }
                return new LRESULT(0);

            case PInvoke.WM_DEVICECHANGE:
                if ((uint)wParam.Value is PInvoke.DBT_DEVICEARRIVAL or PInvoke.DBT_DEVICEREMOVECOMPLETE)
                {
                    _log.Write(LogLevel.Debug, "WM_DEVICECHANGE (HID).");
                    _worker.NotifyDeviceChange();
                }
                return new LRESULT(1);

            case PInvoke.WM_POWERBROADCAST:
                if ((uint)wParam.Value == PInvoke.PBT_APMRESUMEAUTOMATIC)
                {
                    _log.Write(LogLevel.Info, "Resumed from sleep.");
                    _worker.NotifyDeviceChange();
                }
                return new LRESULT(1);

            case PInvoke.WM_DESTROY:
                Shutdown();
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            _iconAdded = false;
            AddTrayIcon(); // Explorer restarted
            return new LRESULT(0);
        }
        return null;
    }

    private void Shutdown()
    {
        _exiting = true;
        HotkeyDialog.Current?.Close(saved: false);
        UnregisterHotkeys();
        if (!_devNotify.IsNull)
        {
            PInvoke.UnregisterDeviceNotification(_devNotify);
            _devNotify = default;
        }
        RemoveTrayIcon();
        _hwnd = default;
    }

    // ---- Hotkeys ----

    /// <summary>Registers <see cref="AppConfig.Hotkeys"/>; returns messages for the ones that failed.</summary>
    private List<string> RegisterHotkeys()
    {
        UnregisterHotkeys();
        _plan = HotkeyPlan.Build(_config.Hotkeys);
        var problems = new List<string>(_plan.Errors);
        foreach (var entry in _plan.Entries)
        {
            if (TryRegister(entry)) continue;
            problems.Add($"{entry.Text} is already used by another app; that hotkey is off.");
        }
        foreach (var p in problems) _log.Write(LogLevel.Warning, p);
        return problems;
    }

    private bool TryRegister(HotkeyPlanEntry entry)
    {
        var mods = (HOT_KEY_MODIFIERS)((uint)entry.Hotkey.Modifiers | ModNoRepeat);
        if (!PInvoke.RegisterHotKey(_hwnd, entry.Id, mods, entry.Hotkey.VirtualKey)) return false;
        _registered.Add(entry.Id);
        return true;
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _registered) PInvoke.UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    private void OnHotkey(int id)
    {
        var entry = _plan.Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return;
        _log.Write(LogLevel.Debug, $"Hotkey {entry.Text} → {entry.Action}.");
        switch (entry.Action.Kind)
        {
            case HotkeyActionKind.NextProfile: _worker.RequestNextProfile(); break;
            case HotkeyActionKind.NextDpiSlot: _worker.RequestNextDpiSlot(); break;
            default: _worker.RequestProfile(entry.Action.Profile); break;
        }
    }

    // IHotkeyDialogHost

    void IHotkeyDialogHost.Opening() => UnregisterHotkeys(); // let the hotkey boxes see the keys

    string? IHotkeyDialogHost.TryApply(List<HotkeyBinding> bindings)
    {
        var plan = HotkeyPlan.Build(bindings);
        if (plan.Errors.Count > 0) return plan.Errors[0];
        foreach (var entry in plan.Entries)
        {
            if (TryRegister(entry)) continue;
            UnregisterHotkeys();
            return $"{entry.Text} is already used by another app. Pick a different combination.";
        }

        _plan = plan;
        _config.Hotkeys = bindings;
        try
        {
            ConfigStore.Save(_configPath, _config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Error, $"Saving config failed: {ex.Message}");
            ShowBalloon("ProfileHop", $"Hotkeys are active but could not be saved: {ex.Message}", warning: true);
        }
        _log.Write(LogLevel.Info, $"Hotkeys saved: {string.Join(", ", plan.Entries.Select(e => $"{e.Text}={e.Action}"))}.");
        return null;
    }

    void IHotkeyDialogHost.Closed(bool saved)
    {
        if (saved || _exiting) return;
        var problems = RegisterHotkeys(); // cancelled: put the old ones back
        if (problems.Count > 0) ShowBalloon("ProfileHop", string.Join("\n", problems), warning: true);
    }

    AppConfig IHotkeyDialogHost.Config => _config;

    ProfileHop.Core.ProfileState? IHotkeyDialogHost.LastState => _lastState;

    // ---- Device notifications ----

    private void RegisterDeviceNotifications()
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE_W
        {
            dbcc_size = (uint)sizeof(DEV_BROADCAST_DEVICEINTERFACE_W),
            dbcc_devicetype = DbtDevtypDeviceInterface,
            dbcc_classguid = HidInterfaceGuid,
        };
        _devNotify = PInvoke.RegisterDeviceNotification((HANDLE)(nint)_hwnd.Value, &filter,
            REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_devNotify.IsNull) _log.Write(LogLevel.Warning, $"RegisterDeviceNotification failed: {Marshal.GetLastPInvokeError()}");
    }

    // ---- Tray icon ----

    private void OnWorkerStatus(WorkerStatus status) => PInvoke.PostMessage(_hwnd, WmStatus, default, default); // marshal to UI

    private void OnWorkerDevices(IReadOnlyList<ProfileHop.Core.KnownDevice> devices) => PInvoke.PostMessage(_hwnd, WmDevices, default, default);

    private void ShowStatus(WorkerStatus status)
    {
        _rescanPending = false; // a rescan always ends with a status; WmDevices (posted first) already reported success
        var previous = _shown;
        _shown = status;
        if (status.State is { } state) _lastState = state;
        UpdateIcon();

        var switched = status.IsReachable && previous.IsReachable
            && status.State!.CurrentProfile != previous.State!.CurrentProfile;
        if (status.Notice is { } notice)
        {
            if (_config.Balloon) ShowBalloon("ProfileHop", notice, warning: true);
        }
        else if (switched)
        {
            if (_config.Sound) PInvoke.MessageBeep(MESSAGEBOX_STYLE.MB_OK);
            if (_config.Balloon) ShowBalloon("ProfileHop", Tooltip()["ProfileHop: ".Length..], warning: false);
        }
        else if (status.Kind == WorkerStatusKind.Unreachable && previous.Kind != WorkerStatusKind.Unreachable && _config.Balloon)
        {
            ShowBalloon("ProfileHop", "Mouse not reachable. The next hotkey press retries.", warning: true);
        }
    }

    private string Tooltip() => TrayText.Tooltip(_shown, _config.Profiles);

    private void AddTrayIcon()
    {
        EnsureIcon();
        var nid = NewNotifyData(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        nid.uCallbackMessage = WmTray;
        nid.hIcon = _icon;
        nid.szTip = Tooltip();
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &nid))
        {
            _log.Write(LogLevel.Warning, "Shell_NotifyIcon(NIM_ADD) failed; will retry when Explorer restarts.");
            return;
        }
        nid.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &nid);
        _iconAdded = true;
    }

    private void UpdateIcon()
    {
        var old = EnsureIcon();
        var nid = NewNotifyData(NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        nid.hIcon = _icon;
        nid.szTip = Tooltip();
        if (_iconAdded) PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &nid);
        else AddTrayIcon();
        if (!old.IsNull) PInvoke.DestroyIcon(old);
    }

    /// <summary>Creates the HICON only when glyph or colour changed; returns the replaced icon (to destroy) or null.</summary>
    private HICON EnsureIcon()
    {
        var key = (TrayText.IconGlyph(_shown), _shown.IsReachable);
        if (!_icon.IsNull && key == _iconKey) return default;
        var old = _icon;
        _icon = TrayIconRenderer.Create(key.Item1, key.Item2);
        _iconKey = key;
        return old;
    }

    private void RemoveTrayIcon()
    {
        if (_iconAdded)
        {
            var nid = NewNotifyData(0);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &nid);
            _iconAdded = false;
        }
        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }

    private void ShowBalloon(string title, string text, bool warning)
    {
        if (!_iconAdded) return;
        var nid = NewNotifyData(NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
        nid.szInfoTitle = Truncate(title, 63);
        nid.szInfo = Truncate(text, 255);
        nid.dwInfoFlags = (warning ? NOTIFY_ICON_INFOTIP_FLAGS.NIIF_WARNING : NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO)
            | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_RESPECT_QUIET_TIME;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &nid);
    }

    private NOTIFYICONDATAW NewNotifyData(NOTIFY_ICON_DATA_FLAGS flags) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "…");

    // ---- Menu ----

    private void ShowMenu(int x, int y)
    {
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            Append(menu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, Tooltip().Replace("&", "&&", StringComparison.Ordinal));
            Append(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            var active = _shown.IsReachable ? _shown.State!.CurrentProfile : 0;
            foreach (var p in ProfileDisplay.Available(_lastState, _config).Profiles)
            {
                var flags = MENU_ITEM_FLAGS.MF_STRING | (p == active ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
                Append(menu, flags, (nuint)(CmdProfileBase + p), TrayText.MenuLabel(p, _config.Profiles, _lastState?.ProfileNames));
            }
            Append(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            // Filled on WM_INITMENUPOPUP from the worker's cached device list; the request below reads the last scan
            // (no device I/O) so the list is fresh by the time the submenu is opened.
            _deviceMenu = PInvoke.CreatePopupMenu();
            _deviceMenuFilled = false;
            _worker.RequestDevices(rescan: false);
            Append(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)(nint)_deviceMenu.Value, "&Device");
            Append(menu, MENU_ITEM_FLAGS.MF_STRING, CmdHotkeys, "&Hotkeys…");
            Append(menu, MENU_ITEM_FLAGS.MF_STRING, CmdReload, "&Reload config");
            Append(menu, MENU_ITEM_FLAGS.MF_STRING, CmdOpenFolder, "&Open config folder");
            Append(menu, MENU_ITEM_FLAGS.MF_STRING | (AutostartEnabled() ? MENU_ITEM_FLAGS.MF_CHECKED : 0), CmdAutostart, "&Start with Windows");
            Append(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            Append(menu, MENU_ITEM_FLAGS.MF_STRING, CmdExit, "E&xit");

            PInvoke.SetForegroundWindow(_hwnd); // so the menu closes when clicking elsewhere
            // No TPM_NONOTIFY: WM_INITMENUPOPUP is needed for the Device submenu. TPM_RETURNCMD still suppresses WM_COMMAND.
            var cmd = PInvoke.TrackPopupMenu(menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON,
                x, y, 0, _hwnd, null).Value;
            PInvoke.PostMessage(_hwnd, PInvoke.WM_NULL, default, default);
            _deviceMenu = default; // destroyed with its parent below
            if (cmd != 0) Execute(cmd);
        }
        finally
        {
            _deviceMenu = default;
            PInvoke.DestroyMenu(menu);
        }
    }

    private void FillDeviceMenu()
    {
        _deviceMenuFilled = true;
        var devices = _worker.Devices;
        _deviceItems = TrayText.DeviceMenu(devices, _config.Device);
        Append(_deviceMenu, MENU_ITEM_FLAGS.MF_STRING | (_config.Device is null ? MENU_ITEM_FLAGS.MF_CHECKED : 0), CmdDeviceAuto, "&Automatic");
        Append(_deviceMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
        if (devices is null) Append(_deviceMenu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, "Looking for devices…");
        else if (devices.Count == 0 && _deviceItems.Count == 0)
            Append(_deviceMenu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, "No devices found");
        for (var i = 0; i < _deviceItems.Count && CmdDeviceBase + i < CmdProfileBase; i++)
        {
            var item = _deviceItems[i];
            Append(_deviceMenu, MENU_ITEM_FLAGS.MF_STRING | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : 0), (nuint)(CmdDeviceBase + i), item.Label);
        }
        Append(_deviceMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
        Append(_deviceMenu, MENU_ITEM_FLAGS.MF_STRING | (_rescanPending ? MENU_ITEM_FLAGS.MF_GRAYED : 0), CmdRescan,
            _rescanPending ? "Rescanning…" : "&Rescan");
    }

    private void OnDevicesListed()
    {
        if (!_rescanPending) return;
        _rescanPending = false;
        var count = _worker.Devices?.Count ?? 0;
        ShowBalloon("ProfileHop", count switch
        {
            0 => "Rescan found no device with onboard profiles.",
            1 => "Rescan found 1 device. Pick it under Device.",
            _ => $"Rescan found {count} devices. Pick one under Device.",
        }, warning: count == 0);
    }

    private void SelectDevice(string? key)
    {
        if (string.Equals(key, _config.Device, StringComparison.OrdinalIgnoreCase)) return;
        _config.Device = key;
        try
        {
            ConfigStore.Save(_configPath, _config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Error, $"Saving config failed: {ex.Message}");
            ShowBalloon("ProfileHop", $"Device changed but could not be saved: {ex.Message}", warning: true);
        }
        _worker.Reconfigure(key, _config.ForceOnboardMode); // drops the connection; the worker reconnects and refreshes
    }

    private static void Append(HMENU menu, MENU_ITEM_FLAGS flags, nuint id, string? text)
    {
        fixed (char* p = text)
        {
            PInvoke.AppendMenu(menu, flags, id, p);
        }
    }

    private void Execute(int cmd)
    {
        switch (cmd)
        {
            case CmdHotkeys:
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) HotkeyDialog.Show(this);
                else ShowBalloon("ProfileHop", "The Hotkeys window needs Windows 10; edit config.json instead.", warning: true);
                break;
            case CmdReload:
                ReloadConfig();
                break;
            case CmdOpenFolder:
                OpenConfigFolder();
                break;
            case CmdAutostart:
                ToggleAutostart();
                break;
            case CmdExit:
                PInvoke.DestroyWindow(_hwnd);
                break;
            case CmdDeviceAuto:
                SelectDevice(null);
                break;
            case CmdRescan:
                if (_rescanPending) break;
                _rescanPending = true;
                _worker.RequestDevices(rescan: true);
                break;
            case >= CmdDeviceBase and < CmdProfileBase:
                if (cmd - CmdDeviceBase < _deviceItems.Count) SelectDevice(_deviceItems[cmd - CmdDeviceBase].Key);
                break;
            case >= CmdProfileBase:
                _worker.RequestProfile(cmd - CmdProfileBase);
                break;
        }
    }

    private void ReloadConfig()
    {
        if (HotkeyDialog.Current is not null) return; // the dialog owns the hotkeys right now
        var loaded = ConfigStore.Load(_configPath);
        var (oldDevice, oldForce) = (_config.Device, _config.ForceOnboardMode);
        _config = loaded.Config;
        _log.MinLevel = _config.LogLevel;
        _worker.CycleOverride = _config.Cycle;
        _worker.ConfiguredProfiles = ProfileDisplay.ConfiguredNumbers(_config);
        if (!string.Equals(oldDevice, _config.Device, StringComparison.OrdinalIgnoreCase) || oldForce != _config.ForceOnboardMode)
            _worker.Reconfigure(_config.Device, _config.ForceOnboardMode);
        var problems = new List<string>(loaded.Errors);
        problems.AddRange(RegisterHotkeys());
        UpdateIcon();
        _log.Write(LogLevel.Info, "Config reloaded.");
        ShowBalloon("ProfileHop", problems.Count > 0 ? string.Join("\n", problems) : "Config reloaded.", warning: problems.Count > 0);
    }

    private void OpenConfigFolder()
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        fixed (char* verb = "open")
        fixed (char* file = dir)
        {
            PInvoke.ShellExecute(_hwnd, verb, file, null, null, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
        }
    }

    private bool AutostartEnabled()
    {
        try
        {
            return _autostart.IsEnabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private void ToggleAutostart()
    {
        try
        {
            var on = _autostart.Toggle();
            _log.Write(LogLevel.Info, $"Start with Windows: {(on ? "on" : "off")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Write(LogLevel.Error, $"Changing autostart failed: {ex.Message}");
            ShowBalloon("ProfileHop", $"Could not change autostart: {ex.Message}", warning: true);
        }
    }
}
