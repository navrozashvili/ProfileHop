using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Config;

public enum HotkeyActionKind
{
    NextProfile,
    NextDpiSlot,
    Profile,
}

/// <summary>What a hotkey does. <see cref="Profile"/> is the 1-based target for <see cref="HotkeyActionKind.Profile"/>.</summary>
public readonly record struct HotkeyAction(HotkeyActionKind Kind, int Profile)
{
    public static bool TryParse(string? text, out HotkeyAction action, [NotNullWhen(false)] out string? error)
    {
        action = default;
        error = null;
        var parts = (text ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["next-profile"]:
                action = new HotkeyAction(HotkeyActionKind.NextProfile, 0);
                return true;
            case ["next-dpi-slot"]:
                action = new HotkeyAction(HotkeyActionKind.NextDpiSlot, 0);
                return true;
            case ["profile", var n] when int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p >= 1:
                action = new HotkeyAction(HotkeyActionKind.Profile, p);
                return true;
            default:
                error = $"Unknown hotkey action '{text}'. Use \"next-profile\", \"next-dpi-slot\" or \"profile N\".";
                return false;
        }
    }
}

/// <param name="Id">RegisterHotKey id (1-based, in config order).</param>
/// <param name="Text">Canonical key text for messages, e.g. "Ctrl+Alt+F9".</param>
public sealed record HotkeyPlanEntry(int Id, Hotkey Hotkey, HotkeyAction Action, string Text);

/// <summary>Turns config bindings into hotkeys to register, collecting the ones that can't be used.</summary>
public sealed record HotkeyPlan(IReadOnlyList<HotkeyPlanEntry> Entries, IReadOnlyList<string> Errors)
{
    public static HotkeyPlan Build(IReadOnlyList<HotkeyBinding> bindings)
    {
        var entries = new List<HotkeyPlanEntry>();
        var errors = new List<string>();
        var used = new HashSet<Hotkey>();
        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Keys)) continue; // disabled
            if (!HotkeyParser.TryParse(b.Keys, out var hotkey, out var keyError))
            {
                errors.Add(keyError);
                continue;
            }
            if (!HotkeyAction.TryParse(b.Action, out var action, out var actionError))
            {
                errors.Add($"{hotkey}: {actionError}");
                continue;
            }
            if (!used.Add(hotkey))
            {
                errors.Add($"Hotkey {hotkey} is configured more than once; only the first is used.");
                continue;
            }
            entries.Add(new HotkeyPlanEntry(entries.Count + 1, hotkey, action, hotkey.ToString()));
        }
        return new HotkeyPlan(entries, errors);
    }
}
