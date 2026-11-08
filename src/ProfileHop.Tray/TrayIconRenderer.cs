using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ProfileHop.Tray;

/// <summary>Draws the tray icon: a rounded square with the profile number. Blue when reachable, grey otherwise.</summary>
internal static class TrayIconRenderer
{
    private const uint ActiveColor = 0x1F6FEB;   // 0xRRGGBB, BGRA in memory
    private const uint InactiveColor = 0x7A7A7A;

    /// <summary>
    /// Small-icon size (SM_CXSMICON) at the DPI of <paramref name="hwnd"/>'s monitor. The process is per-monitor aware,
    /// so plain GetSystemMetrics would report the size for the DPI at startup only.
    /// </summary>
    public static int IconSize(HWND hwnd)
    {
        int size;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            var dpi = PInvoke.GetDpiForWindow(hwnd);
            size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi == 0 ? 96 : dpi);
        }
        else
        {
            size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        }
        return size > 0 ? size : 16;
    }

    /// <summary>Creates a new <paramref name="size"/>-pixel HICON; the caller destroys it with DestroyIcon.</summary>
    public static unsafe HICON Create(string glyph, bool active, int size)
    {

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = size;
        bmi.bmiHeader.biHeight = -size; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        var dc = PInvoke.CreateCompatibleDC(default);
        void* bits;
        var color = PInvoke.CreateDIBSection(dc, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
        var mask = PInvoke.CreateBitmap(size, size, 1, 1, null);
        HFONT font = default;
        try
        {
            var px = (uint*)bits;
            var bg = active ? ActiveColor : InactiveColor;
            for (var i = 0; i < size * size; i++) px[i] = bg;

            var oldBitmap = PInvoke.SelectObject(dc, color);
            fixed (char* face = "Segoe UI")
            {
                var height = glyph.Length > 1 ? size * 6 / 10 : size * 85 / 100;
                font = PInvoke.CreateFont(-height, 0, 0, 0, 700, 0, 0, 0, FONT_CHARSET.DEFAULT_CHARSET,
                    FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS, FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                    FONT_QUALITY.ANTIALIASED_QUALITY, 0, face);
            }
            var oldFont = PInvoke.SelectObject(dc, font);
            PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
            PInvoke.SetTextColor(dc, new COLORREF(0x00FFFFFF));
            var rect = new RECT { left = 0, top = 0, right = size, bottom = size };
            fixed (char* text = glyph)
            {
                PInvoke.DrawText(dc, text, glyph.Length, &rect,
                    DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }
            PInvoke.GdiFlush();
            PInvoke.SelectObject(dc, oldFont);
            PInvoke.SelectObject(dc, oldBitmap);

            // GDI text leaves alpha at 0: make everything opaque, then cut the corners (premultiplied: rgb 0 too).
            for (var i = 0; i < size * size; i++) px[i] |= 0xFF000000;
            int last = size - 1;
            px[0] = px[last] = px[last * size] = px[last * size + last] = 0;

            var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
            return PInvoke.CreateIconIndirect(&info);
        }
        finally
        {
            if (!font.IsNull) PInvoke.DeleteObject(font);
            PInvoke.DeleteObject(color);
            PInvoke.DeleteObject(mask);
            PInvoke.DeleteDC(dc);
        }
    }
}
