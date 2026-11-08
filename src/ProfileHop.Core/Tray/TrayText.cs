using ProfileHop.Core.Config;

namespace ProfileHop.Core.Tray;

/// <summary>Text shown by the tray icon: tooltip, menu labels and the glyph drawn into the icon.</summary>
public static class TrayText
{
    /// <summary>NOTIFYICONDATAW.szTip holds 128 chars including the terminator.</summary>
    public const int MaxTooltipLength = 127;

    public static string Tooltip(WorkerStatus status, IReadOnlyList<ProfileName> names)
    {
        var text = status.Kind switch
        {
            WorkerStatusKind.Starting => "ProfileHop: connecting…",
            WorkerStatusKind.Unreachable => "ProfileHop: Mouse not reachable",
            _ => ProfileTip(status.State!, names),
        };
        return text.Length <= MaxTooltipLength ? text : string.Concat(text.AsSpan(0, MaxTooltipLength - 1), "…");
    }

    public static string? NameOf(int profile, IReadOnlyList<ProfileName> names)
    {
        foreach (var n in names)
        {
            if (n.Number == profile && !string.IsNullOrWhiteSpace(n.Name)) return n.Name;
        }
        return null;
    }

    /// <summary>Menu item text for a profile; '&amp;' is doubled so it is not taken as a mnemonic.</summary>
    public static string MenuLabel(int profile, IReadOnlyList<ProfileName> names) =>
        $"{profile}  {Escape(NameOf(profile, names) ?? $"Profile {profile}")}";

    /// <summary>Profiles listed in the menu: the device's enabled profiles plus configured names, ascending.</summary>
    public static IReadOnlyList<int> MenuProfiles(IReadOnlyList<int>? enabled, IReadOnlyList<ProfileName> names)
    {
        var set = new SortedSet<int>(enabled ?? []);
        foreach (var n in names)
        {
            if (n.Number > 0) set.Add(n.Number);
        }
        return [.. set];
    }

    public static string IconGlyph(WorkerStatus status) => status.Kind switch
    {
        WorkerStatusKind.Reachable when status.State!.OnboardMode == 2 || status.State.CurrentProfile == 0 => "H", // host mode
        WorkerStatusKind.Reachable => status.State!.CurrentProfile.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WorkerStatusKind.Unreachable => "?",
        _ => "…",
    };

    /// <summary>
    /// Device submenu entries after "Automatic": one per known device ("(in use)" marks the connected one), checked
    /// when it is the preference; a preference that is not in the list is kept as a checked "(not found)" entry.
    /// Labels have '&amp;' doubled.
    /// </summary>
    public static IReadOnlyList<DeviceMenuItem> DeviceMenu(IReadOnlyList<KnownDevice>? devices, string? preference)
    {
        var items = new List<DeviceMenuItem>();
        var found = false;
        foreach (var d in devices ?? [])
        {
            var isPreferred = DeviceKey.Matches(d.Key, preference);
            found |= isPreferred;
            items.Add(new DeviceMenuItem(Escape(d.DisplayName) + (d.IsActive ? "  (in use)" : ""), d.Key, isPreferred));
        }
        if (!string.IsNullOrWhiteSpace(preference) && !found)
            items.Add(new DeviceMenuItem(Escape(preference.Trim()) + " (not found)", preference.Trim(), true));
        return items;
    }

    private static string Escape(string s) => s.Replace("&", "&&", StringComparison.Ordinal);

    private static string ProfileTip(ProfileState state, IReadOnlyList<ProfileName> names)
    {
        if (state.OnboardMode == 2) return "ProfileHop: mouse in host mode (close G HUB)";
        var p = state.CurrentProfile;
        var name = NameOf(p, names);
        var text = name is null ? $"ProfileHop: profile {p}" : $"ProfileHop: {name} (profile {p})";
        return state.PreferredDeviceMissing ? text + ", chosen device not found" : text;
    }
}

/// <summary>One device entry in the tray's Device submenu.</summary>
/// <param name="Label">Menu text ('&amp;' already doubled).</param>
/// <param name="Key">Device key saved to config when the entry is picked.</param>
public sealed record DeviceMenuItem(string Label, string Key, bool Checked);
