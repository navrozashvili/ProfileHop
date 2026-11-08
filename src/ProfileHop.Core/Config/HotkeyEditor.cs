using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Config;

/// <summary>One line of the Hotkeys dialog. <see cref="Hotkey"/> null = disabled.</summary>
public sealed record HotkeyEditorRow(HotkeyAction Action, string Label, Hotkey? Hotkey, bool OnMouse = true);

/// <summary>Model behind the Hotkeys dialog: config → rows, rows → config bindings.</summary>
public static class HotkeyEditor
{
    /// <summary>
    /// Rows: Next profile; one per available profile (<see cref="ProfileDisplay.Available"/> for the mouse's last known
    /// <paramref name="state"/>, null = not reachable yet) plus profiles that only have a saved binding (marked not
    /// <see cref="HotkeyEditorRow.OnMouse"/> when the list came from the mouse); Next DPI slot. Hotkeys are the saved ones.
    /// </summary>
    public static IReadOnlyList<HotkeyEditorRow> Rows(AppConfig config, ProfileState? state) => BuildRows(config, state);

    /// <summary>Same rows with the built-in default hotkeys (<see cref="AppConfig.DefaultHotkey"/>, for "Reset to defaults").</summary>
    public static IReadOnlyList<HotkeyEditorRow> DefaultRows(AppConfig config, ProfileState? state) =>
        [.. BuildRows(config, state).Select(r => r with { Hotkey = r.OnMouse ? AppConfig.DefaultHotkey(r.Action) : null })];

    /// <summary>Rows → config bindings (cleared rows kept with empty keys). Fails on a combo used twice.</summary>
    public static bool TryBuildBindings(
        IReadOnlyList<HotkeyEditorRow> rows,
        [NotNullWhen(true)] out List<HotkeyBinding>? bindings,
        [NotNullWhen(false)] out string? error)
    {
        bindings = null;
        var owner = new Dictionary<Hotkey, string>();
        foreach (var row in rows)
        {
            if (row.Hotkey is not Hotkey hk) continue;
            if (owner.TryGetValue(hk, out var other))
            {
                error = $"{hk} is used for both \"{other}\" and \"{row.Label}\".";
                return false;
            }
            owner[hk] = row.Label;
        }

        bindings = rows.Select(r => new HotkeyBinding(r.Hotkey?.ToString() ?? "", r.Action.ToString())).ToList();
        error = null;
        return true;
    }

    private static List<HotkeyEditorRow> BuildRows(AppConfig config, ProfileState? state)
    {
        var bound = new Dictionary<HotkeyAction, Hotkey>();
        foreach (var b in config.Hotkeys)
        {
            if (string.IsNullOrWhiteSpace(b.Keys)) continue;
            if (!HotkeyAction.TryParse(b.Action, out var action, out _)) continue;
            if (!HotkeyParser.TryParse(b.Keys, out var hk, out _)) continue;
            bound.TryAdd(action, hk);
        }

        var available = ProfileDisplay.Available(state, config);
        var onMouse = new HashSet<int>(available.Profiles);
        var profiles = new SortedSet<int>(available.Profiles);
        foreach (var action in bound.Keys)
        {
            if (action.Kind == HotkeyActionKind.Profile) profiles.Add(action.Profile);
        }

        var rows = new List<HotkeyEditorRow>(profiles.Count + 2);
        Add(new HotkeyAction(HotkeyActionKind.NextProfile, 0), "Next profile", true);
        foreach (var p in profiles)
        {
            var name = ProfileDisplay.NameOf(p, config.Profiles, state?.ProfileNames);
            var number = p.ToString(CultureInfo.InvariantCulture);
            var label = name is null ? $"Profile {number}" : $"Profile {number} — {name}";
            var present = onMouse.Contains(p) || !available.FromMouse;
            Add(new HotkeyAction(HotkeyActionKind.Profile, p), present ? label : label + " (not on mouse)", present);
        }
        Add(new HotkeyAction(HotkeyActionKind.NextDpiSlot, 0), "Next DPI slot", true);
        return rows;

        void Add(HotkeyAction action, string label, bool present) =>
            rows.Add(new HotkeyEditorRow(action, label, bound.TryGetValue(action, out var hk) ? hk : null, present));
    }
}
