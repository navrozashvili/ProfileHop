using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ProfileHop.Tray;

internal interface IHotkeyDialogHost
{
    AppConfig Config { get; }

    /// <summary>Called before the dialog shows: the host releases its global hotkeys so the boxes can capture them.</summary>
    void Opening();

    /// <summary>Registers and persists the bindings. Returns an error to show (dialog stays open) or null on success.</summary>
    string? TryApply(List<HotkeyBinding> bindings);

    /// <summary>The dialog is gone. Not saved → the host re-registers its previous hotkeys.</summary>
    void Closed(bool saved);
}

/// <summary>
/// "Hotkeys…" window: one row per action with a standard hotkey control (msctls_hotkey32) and a Clear button, plus
/// Save / Cancel / Reset to defaults. Built with CreateWindowEx only while open and fully destroyed on close
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

    // Hotkey control messages and rules (commctrl.h).
    private const uint HkmSetHotkey = 0x0401, HkmGetHotkey = 0x0402, HkmSetRules = 0x0403;
    private const uint HkcombNone = 0x1, HkcombS = 0x2;
    private const uint HotkeyfControl = 0x2, HotkeyfAlt = 0x4;
    private const uint IccHotkeyClass = 0x40;
    private const uint SpiGetNonClientMetrics = 0x29;
    private const int ColorBtnFace = 15;
    private const int SsLeft = 0x0, SsCenterImage = 0x200, BsPushButton = 0x0, BsDefPushButton = 0x1;

    private static bool s_classRegistered;
    private static bool s_commonControlsReady;

    private readonly IHotkeyDialogHost _host;
    private readonly AppConfig _config;
    private IReadOnlyList<HotkeyEditorRow> _baseline;
    private readonly List<HWND> _boxes = [];
    private HFONT _font;
    private bool _saved;

    private HotkeyDialog(IHotkeyDialogHost host)
    {
        _host = host;
        _config = host.Config;
        _baseline = HotkeyEditor.Rows(_config);
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

        EnsureCommonControls();
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

    // Lazily, on first open: the tray app never loads comctl32's hotkey class unless the user asks for this window.
    private static void EnsureCommonControls()
    {
        if (s_commonControlsReady) return;
        var icc = new INITCOMMONCONTROLSEX
        {
            dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
            dwICC = (INITCOMMONCONTROLSEX_ICC)IccHotkeyClass,
        };
        s_commonControlsReady = PInvoke.InitCommonControlsEx(&icc);
    }

    private static HINSTANCE Instance => (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;

    [SupportedOSPlatform(MinOs)]
    private void Create()
    {
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

            Hwnd = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME | WINDOW_EX_STYLE.WS_EX_CONTROLPARENT,
                className, title, Style, PInvoke.CW_USEDEFAULT, PInvoke.CW_USEDEFAULT, 100, 100, default, default, Instance, null);
        }
        if (Hwnd.IsNull) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

        var dpi = PInvoke.GetDpiForWindow(Hwnd);
        if (dpi == 0) dpi = 96;
        int S(int v) => v * (int)dpi / 96;
        _font = CreateMessageFont(dpi);

        const int margin = 12, labelW = 190, boxW = 180, clearW = 70, rowH = 24, gap = 8, buttonW = 96, buttonH = 26;
        var y = margin;
        AddChild("STATIC", "Click a box and press the new key combination. Clear turns a hotkey off.", SsLeft,
            S(margin), S(y), S(labelW + gap + boxW + gap + clearW), S(rowH), 0, tabStop: false);
        y += rowH + gap;

        for (var i = 0; i < _baseline.Count; i++)
        {
            AddChild("STATIC", _baseline[i].Label, SsLeft | SsCenterImage, S(margin), S(y), S(labelW), S(rowH), 0, tabStop: false);
            var box = AddChild("msctls_hotkey32", null, 0, S(margin + labelW + gap), S(y), S(boxW), S(rowH), IdHotkeyBase + i, tabStop: true,
                WINDOW_EX_STYLE.WS_EX_CLIENTEDGE);
            PInvoke.SendMessage(box, HkmSetRules, HkcombNone | HkcombS, (nint)(HotkeyfControl | HotkeyfAlt));
            _boxes.Add(box);
            AddChild("BUTTON", "Clear", BsPushButton, S(margin + labelW + gap + boxW + gap), S(y), S(clearW), S(rowH), IdClearBase + i, tabStop: true);
            y += rowH + gap;
        }
        LoadRows(_baseline);

        y += gap;
        var contentW = labelW + gap + boxW + gap + clearW;
        AddChild("BUTTON", "Reset to defaults", BsPushButton, S(margin), S(y), S(buttonW + 30), S(buttonH), IdReset, tabStop: true);
        AddChild("BUTTON", "Save", BsDefPushButton, S(margin + contentW - 2 * buttonW - gap), S(y), S(buttonW), S(buttonH), IdSave, tabStop: true);
        AddChild("BUTTON", "Cancel", BsPushButton, S(margin + contentW - buttonW), S(y), S(buttonW), S(buttonH), IdCancel, tabStop: true);
        y += buttonH + margin;

        var rect = new RECT { left = 0, top = 0, right = S(margin + contentW + margin), bottom = S(y) };
        PInvoke.AdjustWindowRectExForDpi(&rect, Style, false, WINDOW_EX_STYLE.WS_EX_DLGMODALFRAME | WINDOW_EX_STYLE.WS_EX_CONTROLPARENT, dpi);
        var w = rect.right - rect.left;
        var h = rect.bottom - rect.top;
        var screenW = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
        var screenH = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
        PInvoke.SetWindowPos(Hwnd, default, Math.Max(0, (screenW - w) / 2), Math.Max(0, (screenH - h) / 2), w, h,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetForegroundWindow(Hwnd);
        if (_boxes.Count > 0) PInvoke.SetFocus(_boxes[0]);
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
            var value = rows[i].Hotkey is Hotkey hk ? HotkeyControlCodec.Encode(hk) : (ushort)0;
            PInvoke.SendMessage(_boxes[i], HkmSetHotkey, value, 0);
        }
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
                LoadRows(HotkeyEditor.DefaultRows(_config));
                break;
            case >= IdClearBase when id - IdClearBase < _boxes.Count:
                PInvoke.SendMessage(_boxes[id - IdClearBase], HkmSetHotkey, 0, 0);
                PInvoke.SetFocus(_boxes[id - IdClearBase]);
                break;
        }
    }

    private void Save()
    {
        var rows = new List<HotkeyEditorRow>(_baseline.Count);
        for (var i = 0; i < _baseline.Count; i++)
        {
            var row = _baseline[i];
            var value = (ushort)(PInvoke.SendMessage(_boxes[i], HkmGetHotkey, 0, 0).Value & 0xFFFF);

            // Untouched row: keep the original exactly (the control can't show a Win modifier).
            if (row.Hotkey is Hotkey original && value == HotkeyControlCodec.Encode(original))
            {
                rows.Add(row);
                continue;
            }
            if (!HotkeyControlCodec.TryDecode(value, out var hotkey, out var error))
            {
                Error($"{row.Label}: {error}");
                PInvoke.SetFocus(_boxes[i]);
                return;
            }
            rows.Add(row with { Hotkey = hotkey });
        }

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
        Hwnd = default;
        Current = null;
        _host.Closed(_saved);
    }
}
