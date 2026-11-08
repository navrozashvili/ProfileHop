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
        $"{profile}  {(NameOf(profile, names) ?? $"Profile {profile}").Replace("&", "&&", StringComparison.Ordinal)}";

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
        WorkerStatusKind.Reachable => status.State!.CurrentProfile.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WorkerStatusKind.Unreachable => "?",
        _ => "…",
    };

    private static string ProfileTip(ProfileState state, IReadOnlyList<ProfileName> names)
    {
        var p = state.CurrentProfile;
        var name = NameOf(p, names);
        var text = name is null ? $"ProfileHop: profile {p}" : $"ProfileHop: {name} (profile {p})";
        return state.OnboardMode == 2 ? text + ", host mode" : text;
    }
}
