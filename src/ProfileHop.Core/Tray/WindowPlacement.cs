namespace ProfileHop.Core.Tray;

/// <summary>Where to put a window on a monitor (virtual-screen coordinates; other monitors can be negative).</summary>
public static class WindowPlacement
{
    /// <summary>
    /// Top-left corner that centres a <paramref name="width"/> × <paramref name="height"/> window in the work area
    /// (<paramref name="left"/>, <paramref name="top"/>)–(<paramref name="right"/>, <paramref name="bottom"/>). A window
    /// larger than the work area starts at its top-left so the caption stays reachable.
    /// </summary>
    public static (int X, int Y) Center(int left, int top, int right, int bottom, int width, int height) =>
        (left + Math.Max(0, (right - left - width) / 2), top + Math.Max(0, (bottom - top - height) / 2));
}
