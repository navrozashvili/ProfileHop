using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Config;

public class HotkeyEditorTests
{
    private static Hotkey H(string text) => HotkeyParser.Parse(text);

    private static ProfileState Mouse(int[] enabled, Dictionary<int, string>? names = null) =>
        new(1, enabled[0], enabled, 0) { ProfileNames = names ?? [] };

    private static readonly Dictionary<int, string> RealNames = new() { [1] = "400", [2] = "600", [3] = "800", [4] = "1600", [5] = "3200" };

    private static List<HotkeyBinding> OldDefaults() =>
    [
        new("Ctrl+Alt+F9", "next-profile"),
        new("Ctrl+Alt+F10", "profile 1"),
        new("Ctrl+Alt+F11", "profile 2"),
        new("Ctrl+Alt+F12", "next-dpi-slot"),
    ];

    [Fact]
    public void Rows_from_default_config_without_mouse_state_list_profiles_1_to_5()
    {
        var rows = HotkeyEditor.Rows(new AppConfig(), state: null);

        Assert.Equal(["Next profile", "Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5", "Next DPI slot"],
            rows.Select(r => r.Label));
        Assert.Equal([H("Ctrl+Alt+F9"), H("Ctrl+Alt+1"), H("Ctrl+Alt+2"), H("Ctrl+Alt+3"), H("Ctrl+Alt+4"), H("Ctrl+Alt+5"), null],
            rows.Select(r => r.Hotkey));
        Assert.All(rows, r => Assert.True(r.OnMouse));
        Assert.Equal(new HotkeyAction(HotkeyActionKind.Profile, 2), rows[2].Action);
    }

    [Fact]
    public void Without_mouse_state_the_profiles_come_from_config_hotkeys()
    {
        var config = new AppConfig { Hotkeys = [new("Ctrl+Alt+F5", "profile 4"), new("", "profile 2")] };

        var rows = HotkeyEditor.Rows(config, state: null);

        Assert.Equal(["Next profile", "Profile 2", "Profile 4", "Next DPI slot"], rows.Select(r => r.Label));
        Assert.Equal([null, null, H("Ctrl+Alt+F5"), null], rows.Select(r => r.Hotkey));
    }

    [Fact]
    public void Without_mouse_state_or_profile_hotkeys_falls_back_to_1_to_5()
    {
        var rows = HotkeyEditor.Rows(new AppConfig { Hotkeys = [] }, state: null);

        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Hotkey));
    }

    [Fact]
    public void Rows_list_the_mouse_profiles_with_mouse_names()
    {
        var rows = HotkeyEditor.Rows(new AppConfig(), Mouse([1, 2, 3, 4, 5], RealNames));

        Assert.Equal(["Next profile", "Profile 1 — 400", "Profile 2 — 600", "Profile 3 — 800", "Profile 4 — 1600", "Profile 5 — 3200", "Next DPI slot"],
            rows.Select(r => r.Label));
    }

    [Fact]
    public void Config_name_overrides_the_mouse_name()
    {
        var config = new AppConfig { Profiles = [new(5, "Aim"), new(4, "")] };

        var rows = HotkeyEditor.Rows(config, Mouse([1, 4, 5], RealNames));

        Assert.Equal("Profile 5 — Aim", rows.Single(r => r.Action.Profile == 5 && r.Action.Kind == HotkeyActionKind.Profile).Label);
        Assert.Equal("Profile 4 — 1600", rows.Single(r => r.Action.Profile == 4 && r.Action.Kind == HotkeyActionKind.Profile).Label);
    }

    [Fact]
    public void Bound_profile_that_is_not_on_the_mouse_keeps_its_binding_greyed()
    {
        var config = new AppConfig { Hotkeys = [new("Ctrl+Alt+F9", "next-profile"), new("Ctrl+Alt+7", "profile 7")] };

        var rows = HotkeyEditor.Rows(config, Mouse([1, 2], RealNames));

        var seven = rows.Single(r => r.Action == new HotkeyAction(HotkeyActionKind.Profile, 7));
        Assert.False(seven.OnMouse);
        Assert.Equal("Profile 7 (not on mouse)", seven.Label);
        Assert.Equal(H("Ctrl+Alt+7"), seven.Hotkey);
        Assert.Equal(["Next profile", "Profile 1 — 400", "Profile 2 — 600", "Profile 7 (not on mouse)", "Next DPI slot"], rows.Select(r => r.Label));
    }

    [Fact]
    public void Saved_hotkeys_are_kept_and_new_mouse_profiles_get_empty_rows()
    {
        var config = new AppConfig { Profiles = [new(1, "Normal"), new(2, "Game")], Hotkeys = OldDefaults() };

        var rows = HotkeyEditor.Rows(config, Mouse([1, 2, 3, 4, 5], RealNames));

        Assert.Equal(["Next profile", "Profile 1 — Normal", "Profile 2 — Game", "Profile 3 — 800", "Profile 4 — 1600", "Profile 5 — 3200", "Next DPI slot"],
            rows.Select(r => r.Label));
        Assert.Equal([H("Ctrl+Alt+F9"), H("Ctrl+Alt+F10"), H("Ctrl+Alt+F11"), null, null, null, H("Ctrl+Alt+F12")], rows.Select(r => r.Hotkey));
    }

    [Fact]
    public void Single_enabled_profile_on_the_mouse_uses_the_configured_profiles_like_the_cycle()
    {
        var rows = HotkeyEditor.Rows(new AppConfig(), Mouse([1], RealNames));

        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.True(r.OnMouse));
        Assert.Equal("Profile 5 — 3200", rows[5].Label);
    }

    [Fact]
    public void Default_rows_have_the_same_rows_with_default_hotkeys()
    {
        var config = new AppConfig { Hotkeys = [.. OldDefaults(), new("Ctrl+Alt+F8", "profile 12")] };
        var state = Mouse([1, 2, 3, 4, 5], RealNames);

        var rows = HotkeyEditor.Rows(config, state);
        var defaults = HotkeyEditor.DefaultRows(config, state);

        Assert.Equal(rows.Select(r => (r.Action, r.Label, r.OnMouse)), defaults.Select(r => (r.Action, r.Label, r.OnMouse)));
        Assert.Equal([H("Ctrl+Alt+F9"), H("Ctrl+Alt+1"), H("Ctrl+Alt+2"), H("Ctrl+Alt+3"), H("Ctrl+Alt+4"), H("Ctrl+Alt+5"), null, null],
            defaults.Select(r => r.Hotkey)); // profile 12 (not on mouse) has no default; DPI slot is off by default
    }

    [Theory]
    [InlineData("next-profile", "Ctrl+Alt+F9")]
    [InlineData("profile 1", "Ctrl+Alt+1")]
    [InlineData("profile 9", "Ctrl+Alt+9")]
    [InlineData("profile 10", null)]
    [InlineData("next-dpi-slot", null)]
    public void Default_hotkey_per_action(string action, string? keys)
    {
        Assert.True(HotkeyAction.TryParse(action, out var a, out _));
        Assert.Equal(keys is null ? null : H(keys), AppConfig.DefaultHotkey(a));
    }

    [Fact]
    public void Save_turns_rows_into_bindings_keeping_cleared_rows_as_empty_keys()
    {
        var rows = HotkeyEditor.Rows(new AppConfig { Hotkeys = OldDefaults() }, state: null);
        var edited = rows.Select((r, i) => i == 3 ? r with { Hotkey = null } : r).ToList();

        Assert.True(HotkeyEditor.TryBuildBindings(edited, out var bindings, out var error), error);
        Assert.Equal(
            [
                new HotkeyBinding("Ctrl+Alt+F9", "next-profile"),
                new HotkeyBinding("Ctrl+Alt+F10", "profile 1"),
                new HotkeyBinding("Ctrl+Alt+F11", "profile 2"),
                new HotkeyBinding("", "next-dpi-slot"),
            ],
            bindings);
        Assert.Empty(HotkeyPlan.Build(bindings).Errors);
    }

    [Fact]
    public void Save_rejects_duplicates_naming_the_combo_and_both_rows()
    {
        var rows = HotkeyEditor.Rows(new AppConfig(), Mouse([1, 2, 3, 4, 5], RealNames));
        var edited = rows.Select((r, i) => i == 1 ? r with { Hotkey = H("Ctrl+Alt+F9") } : r).ToList();

        Assert.False(HotkeyEditor.TryBuildBindings(edited, out _, out var error));
        Assert.Contains("Ctrl+Alt+F9", error);
        Assert.Contains("Next profile", error);
        Assert.Contains("Profile 1 — 400", error);
    }

    [Fact]
    public void Bindings_round_trip_through_rows()
    {
        var config = new AppConfig();
        Assert.True(HotkeyEditor.TryBuildBindings(HotkeyEditor.Rows(config, state: null), out var bindings, out _));
        Assert.Equal(config.Hotkeys, bindings);
    }
}
