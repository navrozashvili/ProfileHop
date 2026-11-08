using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Config;

/// <summary>A hotkey in config.json: <c>keys</c> like "Ctrl+Alt+F9" (empty = disabled) and an <c>action</c>
/// ("next-profile", "next-dpi-slot" or "profile N").</summary>
public sealed record HotkeyBinding(string Keys, string Action);

/// <summary>Contents of <c>%APPDATA%\ProfileHop\config.json</c>. Missing fields keep these defaults.</summary>
public sealed class AppConfig
{
    public List<ProfileName> Profiles { get; set; } = DefaultProfiles();

    /// <summary>Explicit cycle order for "next profile"; null or empty = the mouse's enabled profiles.</summary>
    public List<int>? Cycle { get; set; }

    /// <summary>Remove an entry (or give it empty keys) to disable it; an empty list disables all hotkeys.</summary>
    public List<HotkeyBinding> Hotkeys { get; set; } = DefaultHotkeys();

    /// <summary>Switch the mouse back to onboard mode when G HUB left it in host mode.</summary>
    public bool ForceOnboardMode { get; set; }

    public bool Sound { get; set; }

    public bool Balloon { get; set; }

    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    /// <summary>Which device to control (key chosen from the tray's Device menu); null = automatic.</summary>
    public string? Device { get; set; }

    internal static List<ProfileName> DefaultProfiles() => [new(1, "Normal"), new(2, "Game")];

    internal static List<HotkeyBinding> DefaultHotkeys() =>
    [
        new("Ctrl+Alt+F9", "next-profile"),
        new("Ctrl+Alt+F10", "profile 1"),
        new("Ctrl+Alt+F11", "profile 2"),
        new("Ctrl+Alt+F12", "next-dpi-slot"),
    ];
}
