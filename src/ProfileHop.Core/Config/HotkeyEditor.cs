using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Config;

/// <summary>One line of the Hotkeys dialog. <see cref="Hotkey"/> null = disabled.</summary>
public sealed record HotkeyEditorRow(HotkeyAction Action, string Label, Hotkey? Hotkey);

/// <summary>Model behind the Hotkeys dialog: config → rows, rows → config bindings.</summary>
public static class HotkeyEditor
{
    /// <summary>Rows: Next profile, one per configured profile (plus profiles that only appear in bindings), Next DPI slot.</summary>
    public static IReadOnlyList<HotkeyEditorRow> Rows(AppConfig config) => BuildRows(config, config.Hotkeys);

    /// <summary>Same rows with the built-in default hotkeys (for "Reset to defaults").</summary>
    public static IReadOnlyList<HotkeyEditorRow> DefaultRows(AppConfig config) => BuildRows(config, AppConfig.DefaultHotkeys());

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

    private static List<HotkeyEditorRow> BuildRows(AppConfig config, IReadOnlyList<HotkeyBinding> bindings)
    {
        var bound = new Dictionary<HotkeyAction, Hotkey>();
        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Keys)) continue;
            if (!HotkeyAction.TryParse(b.Action, out var action, out _)) continue;
            if (!HotkeyParser.TryParse(b.Keys, out var hk, out _)) continue;
            bound.TryAdd(action, hk);
        }

        var profiles = new SortedSet<int>(config.Profiles.Select(p => p.Number));
        foreach (var action in bound.Keys)
        {
            if (action.Kind == HotkeyActionKind.Profile) profiles.Add(action.Profile);
        }

        var rows = new List<HotkeyEditorRow>(profiles.Count + 2);
        Add(new HotkeyAction(HotkeyActionKind.NextProfile, 0), "Next profile");
        foreach (var p in profiles)
        {
            var name = TrayText.NameOf(p, config.Profiles);
            var number = p.ToString(CultureInfo.InvariantCulture);
            Add(new HotkeyAction(HotkeyActionKind.Profile, p), name is null ? $"Profile {number}" : $"Profile {number} ({name})");
        }
        Add(new HotkeyAction(HotkeyActionKind.NextDpiSlot, 0), "Next DPI slot");
        return rows;

        void Add(HotkeyAction action, string label) =>
            rows.Add(new HotkeyEditorRow(action, label, bound.TryGetValue(action, out var hk) ? hk : null));
    }
}
