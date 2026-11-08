using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Config;

/// <summary>A hotkey in config.json: <c>keys</c> like "Ctrl+Alt+F9" (empty = disabled) and an <c>action</c>
/// ("next-profile", "previous-profile", "next-dpi-slot" or "profile N").</summary>
public sealed record HotkeyBinding(string Keys, string Action);

/// <summary>Contents of <c>%APPDATA%\ProfileHop\config.json</c>. Missing fields keep these defaults.</summary>
public sealed class AppConfig
{
    public List<ProfileName> Profiles { get; set; } = DefaultProfiles();

    /// <summary>
    /// Explicit cycle order for "next profile"; null or empty = the mouse's enabled profiles, or the configured numbers
    /// (<see cref="Profiles"/> plus "profile N" hotkeys) when the mouse reports fewer than two enabled profiles.
    /// </summary>
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

    /// <summary>Profile for apps with no entry in <see cref="AppProfiles"/>; null = set from the mouse at its first read.</summary>
    public int? DefaultProfile { get; set; }

    /// <summary>Per-app profiles: lower-cased full exe path → profile number (1-based).</summary>
    public Dictionary<string, int> AppProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Switch profiles automatically when the foreground app changes.</summary>
    public bool AutoSwitch { get; set; } = true;

    /// <summary>Show a Windows notification (mouse, profile, DPI) when a profile hotkey switches the profile.</summary>
    public bool SwitchNotification { get; set; } = true;

    private const uint VkF8 = 0x77;
    private const uint VkF9 = 0x78;
    private const int DefaultProfileCount = 5;

    /// <summary>
    /// Built-in hotkey for an action: next profile Ctrl+Alt+F9, previous profile Ctrl+Alt+F8, profile 1–9
    /// Ctrl+Alt+1–9, otherwise none (next DPI slot is off by default).
    /// </summary>
    public static Hotkey? DefaultHotkey(HotkeyAction action) => action.Kind switch
    {
        HotkeyActionKind.NextProfile => new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkF9),
        HotkeyActionKind.PreviousProfile => new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkF8),
        HotkeyActionKind.Profile when action.Profile is >= 1 and <= 9 =>
            new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)('0' + action.Profile)),
        _ => null,
    };

    /// <summary>No names by default: the tray shows the names stored on the mouse; entries here override them.</summary>
    internal static List<ProfileName> DefaultProfiles() => [];

    internal static List<HotkeyBinding> DefaultHotkeys()
    {
        List<HotkeyAction> actions = [new(HotkeyActionKind.NextProfile, 0), new(HotkeyActionKind.PreviousProfile, 0)];
        for (var p = 1; p <= DefaultProfileCount; p++) actions.Add(new(HotkeyActionKind.Profile, p));
        actions.Add(new(HotkeyActionKind.NextDpiSlot, 0));
        return [.. actions.Select(a => new HotkeyBinding(DefaultHotkey(a)?.ToString() ?? "", a.ToString()))];
    }
}
