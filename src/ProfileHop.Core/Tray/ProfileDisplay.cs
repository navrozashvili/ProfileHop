using ProfileHop.Core.Config;

namespace ProfileHop.Core.Tray;

/// <param name="Profiles">1-based profile numbers to list, ascending.</param>
/// <param name="FromMouse">True when the list comes from the mouse's last known state; false = config fallback.</param>
public sealed record AvailableProfiles(IReadOnlyList<int> Profiles, bool FromMouse);

/// <summary>Which profiles the tray lists and what they are called (menu, tooltip, Hotkeys dialog).</summary>
public static class ProfileDisplay
{
    /// <summary>Profiles listed when nothing else is known: the PRO X2 SUPERSTRIKE has five.</summary>
    public static IReadOnlyList<int> FallbackProfiles { get; } = [1, 2, 3, 4, 5];

    /// <summary>Config <c>profiles</c> name (if non-blank) &gt; name stored on the mouse &gt; null.</summary>
    public static string? NameOf(int profile, IReadOnlyList<ProfileName> config, IReadOnlyDictionary<int, string>? mouse)
    {
        foreach (var n in config)
        {
            if (n.Number == profile && !string.IsNullOrWhiteSpace(n.Name)) return n.Name;
        }
        return mouse is not null && mouse.TryGetValue(profile, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
    }

    /// <summary><see cref="NameOf"/> or "Profile N".</summary>
    public static string Label(int profile, IReadOnlyList<ProfileName> config, IReadOnlyDictionary<int, string>? mouse) =>
        NameOf(profile, config, mouse) ?? $"Profile {profile}";

    /// <summary>
    /// With a mouse state: its enabled profiles; when it lists fewer than two (the real mouse can), the
    /// <see cref="ConfiguredNumbers"/> are added, as "next profile" does (<see cref="ProfileCycle.Candidates"/>).
    /// Without a state: the profiles that config hotkeys jump to, or <see cref="FallbackProfiles"/> if there are none.
    /// </summary>
    public static AvailableProfiles Available(ProfileState? state, AppConfig config)
    {
        if (state is not null)
        {
            var set = new SortedSet<int>(state.EnabledProfiles);
            if (set.Count < 2)
            {
                var configured = ConfiguredNumbers(config);
                if (configured.Count >= 2) set.UnionWith(configured);
            }
            return new AvailableProfiles([.. set], true);
        }
        int[] bound = [.. HotkeyProfiles(config).Distinct().Order()];
        return new AvailableProfiles(bound.Length > 0 ? bound : FallbackProfiles, false);
    }

    /// <summary>Profile numbers named in config <c>profiles</c> or targeted by a <c>profile N</c> hotkey (even a disabled one).</summary>
    public static IReadOnlyList<int> ConfiguredNumbers(AppConfig config) =>
        [.. config.Profiles.Select(p => p.Number).Where(n => n > 0).Concat(HotkeyProfiles(config)).Distinct().Order()];

    private static IEnumerable<int> HotkeyProfiles(AppConfig config)
    {
        foreach (var b in config.Hotkeys)
        {
            if (HotkeyAction.TryParse(b.Action, out var a, out _) && a.Kind == HotkeyActionKind.Profile) yield return a.Profile;
        }
    }
}
