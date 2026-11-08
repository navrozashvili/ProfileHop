using System.Globalization;

namespace ProfileHop.Core;

/// <summary>Onboard state read from the mouse.</summary>
/// <param name="OnboardMode">1 = onboard, 2 = host (G HUB took over).</param>
/// <param name="CurrentProfile">1-based active profile.</param>
/// <param name="EnabledProfiles">1-based enabled profiles from the profile directory, ascending.</param>
/// <param name="DpiIndex">Active DPI slot inside the current profile, or null if not read.</param>
/// <param name="PreferredDeviceMissing">A device was chosen in config but not found; the automatic choice is in use.</param>
public sealed record ProfileState(
    int OnboardMode,
    int CurrentProfile,
    IReadOnlyList<int> EnabledProfiles,
    int? DpiIndex,
    bool PreferredDeviceMissing = false);

/// <summary>A device that qualifies for control (HID++ 2.0+, has OnboardProfiles 0x8100), as seen by the last scan.</summary>
/// <param name="Key">Stable identity saved as config <c>device</c>; see <see cref="DeviceKey"/>.</param>
/// <param name="DisplayName">Menu text, e.g. <c>PRO X2 SUPERSTRIKE (receiver C54D)</c>.</param>
/// <param name="DeviceType">x0005 device type (3 = mouse), or null if unknown.</param>
/// <param name="IsActive">The device the switcher is connected to.</param>
public sealed record KnownDevice(string Key, string DisplayName, byte? DeviceType, bool IsActive);

/// <summary>
/// Device identity for config <c>device</c>: <c>"&lt;USB PID, 4 upper-case hex&gt;/&lt;HID++ device name&gt;"</c>, e.g.
/// <c>"C54D/PRO X2 SUPERSTRIKE"</c>. The PID is the receiver's (or the wired mouse's), so the same mouse over a
/// receiver and over its cable are different keys. Unlike the HID path it survives moving to another USB port and
/// re-pairing to another receiver slot. A device without a name falls back to <c>"C54D/#01"</c> (device index).
/// Matching is case-insensitive. Two identical devices on one receiver share a key; the first one found wins.
/// </summary>
public static class DeviceKey
{
    public static string Format(ushort productId, string? name, byte deviceIndex) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Create(CultureInfo.InvariantCulture, $"{productId:X4}/#{deviceIndex:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"{productId:X4}/{name}");

    public static bool Matches(string key, string? preference) =>
        preference is not null && string.Equals(key, preference.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Menu text: name plus how it is connected (index 0xFF = directly over USB).</summary>
    public static string DisplayName(ushort productId, string? name, byte deviceIndex)
    {
        string label = string.IsNullOrWhiteSpace(name)
            ? string.Create(CultureInfo.InvariantCulture, $"Device #{deviceIndex:X2}")
            : name;
        string via = deviceIndex == 0xFF ? "USB" : "receiver";
        return string.Create(CultureInfo.InvariantCulture, $"{label} ({via} {productId:X4})");
    }
}

/// <summary>
/// What the tray app needs from the mouse. Implemented by MouseController. All methods block and may be slow
/// (wireless wake-up), so call them from a background worker only. Methods connect lazily and throw on failure
/// (any exception = "mouse not reachable" for the UI).
/// </summary>
public interface IProfileSwitcher : IDisposable
{
    ProfileState GetState();

    /// <summary>Activates a 1-based profile, then confirms with getCurrentProfile and returns the new state.</summary>
    ProfileState SetProfile(int profile);

    /// <summary>Moves to the next DPI slot inside the current profile; returns the new slot index.</summary>
    int NextDpiSlot();

    /// <summary>Drops the current connection; the next call reconnects (used on WM_DEVICECHANGE).</summary>
    void MarkStale();

    /// <summary>
    /// Sets the preferred device key (null = automatic) and whether host mode is switched back to onboard on
    /// connect. Drops the current connection; the next call reconnects with the new settings. No device I/O.
    /// </summary>
    void Configure(string? preferredDevice, bool forceOnboardMode);

    /// <summary>
    /// Qualifying devices seen by the last scan. Without <paramref name="rescan"/> there is no device I/O (empty
    /// before the first connect). With it, every receiver and index is probed and the connection re-established;
    /// "nothing found" is an empty list, not an exception.
    /// </summary>
    IReadOnlyList<KnownDevice> ListDevices(bool rescan);

    /// <summary>Raised from a background thread when the mouse reports a profile change (e.g. from a mouse button).</summary>
    event Action<int>? ProfileChangedByDevice;
}
