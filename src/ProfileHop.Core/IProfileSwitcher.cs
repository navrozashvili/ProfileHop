namespace ProfileHop.Core;

/// <summary>Onboard state read from the mouse.</summary>
/// <param name="OnboardMode">1 = onboard, 2 = host (G HUB took over).</param>
/// <param name="CurrentProfile">1-based active profile.</param>
/// <param name="EnabledProfiles">1-based enabled profiles from the profile directory, ascending.</param>
/// <param name="DpiIndex">Active DPI slot inside the current profile, or null if not read.</param>
public sealed record ProfileState(int OnboardMode, int CurrentProfile, IReadOnlyList<int> EnabledProfiles, int? DpiIndex);

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

    /// <summary>Raised from a background thread when the mouse reports a profile change (e.g. from a mouse button).</summary>
    event Action<int>? ProfileChangedByDevice;
}
