using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ProfileHop.Tray;

internal interface IHotkeyDialogHost
{
    AppConfig Config { get; }

    /// <summary>Last state the mouse reported (profile list and names), or null before it was reachable.</summary>
    ProfileHop.Core.ProfileState? LastState { get; }

    /// <summary>Called before the dialog shows: the host releases its global hotkeys so the boxes can capture them.</summary>
    void Opening();

    /// <summary>Registers and persists the bindings. Returns an error to show (dialog stays open) or null on success.</summary>
    string? TryApply(List<HotkeyBinding> bindings);

    /// <summary>The dialog is gone. Not saved → the host re-registers its previous hotkeys.</summary>
    void Closed(bool saved);
}

/// <summary>
/// "Hotkeys…" window: one row per action with a key-capture field and a Clear button, plus Save / Cancel / Reset to
/// defaults. The field is a read-only EDIT subclassed (SetWindowSubclass) to feed its own WM_KEYDOWN / WM_SYSKEYDOWN
/// into <see cref="HotkeyCapture"/>, so any key HotkeyParser names works, including bare F13–F24 and Win combos
/// (msctls_hotkey32 can do neither). No hooks or timers: only the focused field sees keys, and the global hotkeys
/// stay unregistered while the window is open. Built with CreateWindowEx only while open and fully destroyed on close
/// (font included); nothing is cached between openings except the registered window class.
/// </summary>
internal sealed unsafe class HotkeyDialog
{
    private const string ClassName = "ProfileHopHotkeys";
    private const int IdSave = 1;   // IDOK: Enter
    private const int IdCancel = 2; // IDCANCEL: Esc
    private const int IdReset = 3;
    private const int IdHotkeyBase = 100;
    private const int IdClearBase = 200;

    private const uint SpiGetNonClientMetrics = 0x29;
    private const int ColorBtnFace = 15;
    private const int SsLeft = 0x0, SsCenterImage = 0x200, BsPushButton = 0x0, BsDefPushButton = 0x1;
    private const int EsAutoHScroll = 0x80, EsReadOnly = 0x800;

    // Messages and values handled by the capture field (winuser.h).
    private const uint WmSetFocus = 0x0007, WmKillFocus = 0x0008, WmGetDlgCode = 0x0087, WmKeyDown = 0x0100, WmKeyUp = 0x0101,
        WmChar = 0x0102, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105, WmSysChar = 0x0106, WmCtlColorStatic = 0x0138;
    private const int DlgcWantAllKeys = 0x0004, ColorWindow = 5, ColorWindowText = 8;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;

    private static bool s_classRegistered;

    private readonly IHotkeyDialogHost _host;
    private readonly AppConfig _config;
    private IReadOnlyList<HotkeyEditorRow> _baseline;
    private readonly List<HWND> _boxes = [];
    private readonly List<HotkeyCapture> _captures = [];
    private HFONT _font;
    private bool _saved;

    private HotkeyDialog(IHotkeyDialogHost host)
    {
        _host = host;
        _config = host.Config;
        _baseline = HotkeyEditor.Rows(_config, host.LastState);
    }

    public static HotkeyDialog? Current { get; private set; }

    public HWND Hwnd { get; private set; }

    /// <summary>Per-monitor DPI APIs: Windows 10 1607+.</summary>
    public const string MinOs = "windows10.0.14393";

    [SupportedOSPlatform(MinOs)]
    public static void Show(IHotkeyDialogHost host)
    {
        if (Current is { } open)
        {
            PInvoke.SetForegroundWindow(open.Hwnd);
            return;
        }

        var dialog = new HotkeyDialog(host);
        host.Opening();
        Current = dialog;
        try
        {
            dialog.Create();
        }
        catch
        {
            Current = null;
            host.Closed(saved: false);
            throw;
        }
    }

    public void Close(bool saved)
    {
        _saved = saved;
        if (!Hwnd.IsNull) PInvoke.DestroyWindow(Hwnd); // WM_NCDESTROY cleans up
    }

    private static HINSTANCE Instance => (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;

    [SupportedOSPlatform(MinOs)]
    private void Create()
    {
        var work = CursorWorkArea();
        fixed (char* className = ClassName)
        fixed (char* title = "ProfileHop hotkeys")
        {
            if (!s_classRegistered)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = Instance,
                    lpszClassName = className,
                    hbrBackground = (HBRUSH)(nint)(ColorBtnFace + 1), // class brushes use COLOR_x + 1
                    hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                };
                if (PInvoke.RegisterClassEx(&wc) == 0) throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
                s_classRegistered = true;
            }

            // Created (hidden) inside the work area of the monitor under the cursor, so GetDpiForWindow below is that
            // monitor's DPI and layout, font and frame all use it.
            Hwnd = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME | WINDOW_EX_STYLE.WS_EX_CONTROLPARENT,
                className, title, Style, work.left, work.top, 100, 100, default, default, Instance, null);
        }
        if (Hwnd.IsNull) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

        var dpi = PInvoke.GetDpiForWindow(Hwnd);
        if (dpi == 0) dpi = 96;
        int S(int v) => v * (int)dpi / 96;
        _font = CreateMessageFont(dpi);

        const int margin = 12, labelW = 190, boxW = 180, clearW = 70, rowH = 24, gap = 8, buttonW = 96, buttonH = 26;
        var y = margin;
        AddChild("STATIC", "Click a box and press the new key combination (F13–F24 also work alone). "
            + "Backspace or Clear turns a hotkey off, Esc keeps the old one.", SsLeft,
            S(margin), S(y), S(labelW + gap + boxW + gap + clearW), S(2 * rowH), 0, tabStop: false);
        y += 2 * rowH + gap;

        for (var i = 0; i < _baseline.Count; i++)
        {
            var label = AddChild("STATIC", _baseline[i].Label, SsLeft | SsCenterImage, S(margin), S(y), S(labelW), S(rowH), 0, tabStop: false);
            if (!_baseline[i].OnMouse) PInvoke.EnableWindow(label, false); // greyed: binding kept, profile not on the mouse
            var box = AddChild("EDIT", null, EsReadOnly | EsAutoHScroll, S(margin + labelW + gap), S(y), S(boxW), S(rowH), IdHotkeyBase + i,
                tabStop: true, WINDOW_EX_STYLE.WS_EX_CLIENTEDGE);
            _boxes.Add(box);
            _captures.Add(new HotkeyCapture(_baseline[i].Hotkey));
            PInvoke.SetWindowSubclass(box, &FieldProc, (nuint)i, 0);
            AddChild("BUTTON", "Clear", BsPushButton, S(margin + labelW + gap + boxW + gap), S(y), S(clearW), S(rowH), IdClearBase + i, tabStop: true);
            y += rowH + gap;
        }
        LoadRows(_baseline);

        y += gap;
        var contentW = labelW + gap + boxW + gap + clearW;
        AddChild("BUTTON", "Reset to defaults", BsPushButton, S(margin), S(y), S(buttonW + 30), S(buttonH), IdReset, tabStop: true);
        var save = AddChild("BUTTON", "Save", BsDefPushButton, S(margin + contentW - 2 * buttonW - gap), S(y), S(buttonW), S(buttonH), IdSave, tabStop: true);
        AddChild("BUTTON", "Cancel", BsPushButton, S(margin + contentW - buttonW), S(y), S(buttonW), S(buttonH), IdCancel, tabStop: true);
        y += buttonH + margin;

        var rect = new RECT { left = 0, top = 0, right = S(margin + contentW + margin), bottom = S(y) };
        PInvoke.AdjustWindowRectExForDpi(&rect, Style, false, WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME | WINDOW_EX_STYLE.WS_EX_CONTROLPARENT, dpi);
        var w = rect.right - rect.left;
        var h = rect.bottom - rect.top;
        var (x, y0) = WindowPlacement.Center(work.left, work.top, work.right, work.bottom, w, h);
        PInvoke.SetWindowPos(Hwnd, default, x, y0, w, h, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetForegroundWindow(Hwnd);
        // Not a capture field: focus there would start capturing and hide its hotkey behind "Press a key…".
        PInvoke.SetFocus(save);
    }

    /// <summary>Work area of the monitor under the cursor (nearest one if the cursor is off-screen).</summary>
    private static RECT CursorWorkArea()
    {
        PInvoke.GetCursorPos(out var cursor);
        var monitor = PInvoke.MonitorFromPoint(cursor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        return PInvoke.GetMonitorInfo(monitor, &info)
            ? info.rcWork
            : new RECT { right = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN), bottom = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN) };
    }

    private static WINDOW_STYLE Style => WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_SYSMENU | WINDOW_STYLE.WS_POPUP;

    private HWND AddChild(string cls, string? text, int style, int x, int y, int w, int h, int id, bool tabStop, WINDOW_EX_STYLE ex = 0)
    {
        var ws = WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE | (WINDOW_STYLE)(uint)style | (tabStop ? WINDOW_STYLE.WS_TABSTOP : 0);
        HWND child;
        fixed (char* c = cls)
        fixed (char* t = text)
        {
            child = PInvoke.CreateWindowEx(ex, c, t, ws, x, y, w, h, Hwnd, (HMENU)(nint)id, Instance, null);
        }
        if (!_font.IsNull) PInvoke.SendMessage(child, PInvoke.WM_SETFONT, (nuint)(nint)_font.Value, 1);
        return child;
    }

    [SupportedOSPlatform(MinOs)]
    private static HFONT CreateMessageFont(uint dpi)
    {
        var ncm = new NONCLIENTMETRICSW { cbSize = (uint)sizeof(NONCLIENTMETRICSW) };
        return PInvoke.SystemParametersInfoForDpi(SpiGetNonClientMetrics, ncm.cbSize, &ncm, 0, dpi)
            ? PInvoke.CreateFontIndirect(&ncm.lfMessageFont)
            : default;
    }

    private void LoadRows(IReadOnlyList<HotkeyEditorRow> rows)
    {
        _baseline = rows;
        for (var i = 0; i < rows.Count && i < _boxes.Count; i++)
        {
            _captures[i].Set(rows[i].Hotkey);
            ShowField(i);
        }
    }

    private void ShowField(int i)
    {
        fixed (char* text = _captures[i].Text)
        {
            PInvoke.SetWindowText(_boxes[i], text);
        }
    }

    private static HotkeyModifiers HeldModifiers()
    {
        static bool Down(int vk) => PInvoke.GetKeyState(vk) < 0; // high bit = down at the time of the current message
        var mods = HotkeyModifiers.None;
        if (Down(VkControl)) mods |= HotkeyModifiers.Control;
        if (Down(VkMenu)) mods |= HotkeyModifiers.Alt;
        if (Down(VkShift)) mods |= HotkeyModifiers.Shift;
        if (Down(VkLWin) || Down(VkRWin)) mods |= HotkeyModifiers.Win;
        return mods;
    }

    /// <summary>
    /// Subclass procedure of the capture fields (<paramref name="id"/> = row index). Keys go to <see cref="HotkeyCapture"/>;
    /// WM_CHAR / WM_SYSCHAR and the SYSKEY messages are swallowed so Alt and F10 don't reach the menu loop.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT FieldProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam, nuint id, nuint refData)
    {
        var dialog = Current;
        var i = (int)id;
        if (msg == WmNcDestroy)
        {
            PInvoke.RemoveWindowSubclass(hwnd, &FieldProc, id);
            return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);
        }
        if (dialog is null || i >= dialog._captures.Count || dialog._boxes[i] != hwnd)
            return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);

        try
        {
            var capture = dialog._captures[i];
            switch (msg)
            {
                case WmGetDlgCode:
                    // lParam is the MSG about to be handled (null when the dialog manager just asks).
                    var m = (MSG*)lParam.Value;
                    var wants = m is null || m->message is not (WmKeyDown or WmSysKeyDown)
                        || HotkeyCapture.WantsKey((uint)m->wParam.Value, HeldModifiers());
                    return new LRESULT(wants ? DlgcWantAllKeys : 0);
                case WmSetFocus:
                    capture.Begin();
                    dialog.ShowField(i);
                    break; // let EDIT handle focus (caret)
                case WmKillFocus:
                    capture.End();
                    dialog.ShowField(i);
                    break;
                case WmKeyDown or WmSysKeyDown:
                    if (capture.KeyDown((uint)wParam.Value, HeldModifiers()) == CaptureOutcome.NotHandled)
                        break;
                    dialog.ShowField(i);
                    return new LRESULT(0);
                case WmKeyUp or WmSysKeyUp:
                    capture.KeyUp(HeldModifiers());
                    dialog.ShowField(i);
                    return new LRESULT(0);
                case WmChar or WmSysChar:
                    return new LRESULT(0);
            }
        }
        catch (Exception ex)
        {
            PInvoke.MessageBox(dialog.Hwnd, ex.Message, "ProfileHop", MESSAGEBOX_STYLE.MB_ICONERROR);
        }
        return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        var dialog = Current;
        if (dialog is not null && dialog.Hwnd == hwnd)
        {
            try
            {
                switch (msg)
                {
                    case PInvoke.WM_COMMAND:
                        dialog.OnCommand((int)(wParam.Value & 0xFFFF));
                        return new LRESULT(0);
                    case PInvoke.WM_CLOSE:
                        dialog.Close(saved: false);
                        return new LRESULT(0);
                    case WmNcDestroy:
                        dialog.OnDestroyed();
                        break;
                    case WmCtlColorStatic when dialog._boxes.Contains((HWND)(nint)lParam.Value):
                        // Read-only EDITs draw like statics; keep the fields white like normal input boxes.
                        var hdc = (HDC)(nint)wParam.Value;
                        PInvoke.SetBkColor(hdc, (COLORREF)PInvoke.GetSysColor((SYS_COLOR_INDEX)ColorWindow));
                        PInvoke.SetTextColor(hdc, (COLORREF)PInvoke.GetSysColor((SYS_COLOR_INDEX)ColorWindowText));
                        return new LRESULT((nint)PInvoke.GetSysColorBrush((SYS_COLOR_INDEX)ColorWindow).Value);
                }
            }
            catch (Exception ex)
            {
                PInvoke.MessageBox(hwnd, ex.Message, "ProfileHop", MESSAGEBOX_STYLE.MB_ICONERROR);
            }
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private const uint WmNcDestroy = 0x0082;

    private void OnCommand(int id)
    {
        switch (id)
        {
            case IdSave:
                Save();
                break;
            case IdCancel:
                Close(saved: false);
                break;
            case IdReset:
                LoadRows(HotkeyEditor.DefaultRows(_config, _host.LastState));
                break;
            case >= IdClearBase when id - IdClearBase < _boxes.Count:
                _captures[id - IdClearBase].Set(null);
                ShowField(id - IdClearBase);
                break;
        }
    }

    private void Save()
    {
        var rows = new List<HotkeyEditorRow>(_baseline.Count);
        for (var i = 0; i < _baseline.Count; i++)
            rows.Add(_baseline[i] with { Hotkey = _captures[i].Value });

        if (!HotkeyEditor.TryBuildBindings(rows, out var bindings, out var duplicate))
        {
            Error(duplicate);
            return;
        }
        if (_host.TryApply(bindings) is string failure)
        {
            Error(failure);
            return;
        }
        Close(saved: true);
    }

    private void Error(string text) =>
        PInvoke.MessageBox(Hwnd, text, "ProfileHop hotkeys", MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONWARNING);

    private void OnDestroyed()
    {
        if (!_font.IsNull)
        {
            PInvoke.DeleteObject(_font);
            _font = default;
        }
        _boxes.Clear();
        _captures.Clear();
        Hwnd = default;
        Current = null;
        _host.Closed(_saved);
    }
}
