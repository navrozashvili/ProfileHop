using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Config;

public class HotkeyEditorTests
{
    private static Hotkey H(string text) => HotkeyParser.Parse(text);

    [Fact]
    public void Rows_from_default_config()
    {
        var rows = HotkeyEditor.Rows(new AppConfig());

        Assert.Equal(["Next profile", "Profile 1 (Normal)", "Profile 2 (Game)", "Next DPI slot"], rows.Select(r => r.Label));
        Assert.Equal([H("Ctrl+Alt+F9"), H("Ctrl+Alt+F10"), H("Ctrl+Alt+F11"), H("Ctrl+Alt+F12")], rows.Select(r => r.Hotkey));
        Assert.Equal(new HotkeyAction(HotkeyActionKind.Profile, 2), rows[2].Action);
    }

    [Fact]
    public void Rows_follow_configured_profiles_and_show_unbound_as_null()
    {
        var config = new AppConfig
        {
            Profiles = [new(1, "Desk"), new(2, "Game"), new(3, "Aim")],
            Hotkeys = [new("Ctrl+Alt+F9", "next-profile"), new("", "profile 1")],
        };

        var rows = HotkeyEditor.Rows(config);

        Assert.Equal(["Next profile", "Profile 1 (Desk)", "Profile 2 (Game)", "Profile 3 (Aim)", "Next DPI slot"], rows.Select(r => r.Label));
        Assert.Equal([H("Ctrl+Alt+F9"), null, null, null, null], rows.Select(r => r.Hotkey));
    }

    [Fact]
    public void Bound_profile_without_a_name_still_gets_a_row()
    {
        var config = new AppConfig { Hotkeys = [new("Ctrl+Alt+F5", "profile 4")] };

        var rows = HotkeyEditor.Rows(config);

        Assert.Contains(rows, r => r.Label == "Profile 4" && r.Hotkey == H("Ctrl+Alt+F5"));
        Assert.Equal("Next DPI slot", rows[^1].Label);
    }

    [Fact]
    public void Unnamed_profile_label()
    {
        var rows = HotkeyEditor.Rows(new AppConfig { Profiles = [new(1, "")] });
        Assert.Equal("Profile 1", rows[1].Label);
    }

    [Fact]
    public void Default_rows_restore_default_hotkeys_for_current_profiles()
    {
        var config = new AppConfig { Profiles = [new(1, "Desk"), new(2, "Game"), new(3, "Aim")], Hotkeys = [] };

        var rows = HotkeyEditor.DefaultRows(config);

        Assert.Equal([H("Ctrl+Alt+F9"), H("Ctrl+Alt+F10"), H("Ctrl+Alt+F11"), null, H("Ctrl+Alt+F12")], rows.Select(r => r.Hotkey));
    }

    [Fact]
    public void Save_turns_rows_into_bindings_keeping_cleared_rows_as_empty_keys()
    {
        var rows = HotkeyEditor.Rows(new AppConfig());
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
        var rows = HotkeyEditor.Rows(new AppConfig());
        var edited = rows.Select((r, i) => i == 1 ? r with { Hotkey = H("Ctrl+Alt+F9") } : r).ToList();

        Assert.False(HotkeyEditor.TryBuildBindings(edited, out _, out var error));
        Assert.Contains("Ctrl+Alt+F9", error);
        Assert.Contains("Next profile", error);
        Assert.Contains("Profile 1 (Normal)", error);
    }

    [Fact]
    public void Bindings_round_trip_through_rows()
    {
        var config = new AppConfig();
        Assert.True(HotkeyEditor.TryBuildBindings(HotkeyEditor.Rows(config), out var bindings, out _));
        Assert.Equal(config.Hotkeys, bindings);
    }
}
