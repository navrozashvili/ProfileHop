using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class TrayTextTests
{
    private static readonly IReadOnlyList<ProfileName> Names = [new(1, "Normal"), new(2, "Game")];

    [Fact]
    public void Named_profile()
    {
        var status = WorkerStatus.Reachable(new ProfileState(1, 2, [1, 2], null));
        Assert.Equal("ProfileHop: Game (profile 2)", TrayText.Tooltip(status, Names));
    }

    [Fact]
    public void Tooltip_uses_the_name_stored_on_the_mouse()
    {
        var state = new ProfileState(1, 5, [1, 5], 4) { ProfileNames = new Dictionary<int, string> { [5] = "3200" } };
        Assert.Equal("ProfileHop: 3200 (profile 5)", TrayText.Tooltip(WorkerStatus.Reachable(state), []));
        Assert.Equal("ProfileHop: Aim (profile 5)", TrayText.Tooltip(WorkerStatus.Reachable(state), [new(5, "Aim")]));
    }

    [Fact]
    public void Menu_label_uses_config_then_mouse_name()
    {
        var mouse = new Dictionary<int, string> { [2] = "600", [5] = "3200" };
        Assert.Equal("2  Game", TrayText.MenuLabel(2, Names, mouse));
        Assert.Equal("5  3200", TrayText.MenuLabel(5, Names, mouse));
        Assert.Equal("4  Profile 4", TrayText.MenuLabel(4, Names, mouse));
    }

    private static ProfileState Switched(int profile, int? dpiIndex, ushort[]? slots, string? device) =>
        new(1, profile, [1, 2], dpiIndex)
        {
            DeviceName = device,
            ProfileDpis = slots is null ? new Dictionary<int, IReadOnlyList<ushort>>() : new Dictionary<int, IReadOnlyList<ushort>> { [profile] = slots },
        };

    [Fact]
    public void SwitchNotification_names_profile_and_dpi()
    {
        var state = Switched(2, 1, [400, 800, 1600, 0, 0], "PRO X2 SUPERSTRIKE");
        Assert.Equal(("PRO X2 SUPERSTRIKE", "Game (profile 2) · 800 DPI"), TrayText.SwitchNotification(state, Names));
    }

    [Fact]
    public void SwitchNotification_without_dpi_data()
    {
        var state = Switched(2, 1, null, "PRO X2 SUPERSTRIKE");
        Assert.Equal("Game (profile 2)", TrayText.SwitchNotification(state, Names).Text);
    }

    [Fact]
    public void SwitchNotification_without_a_name()
    {
        var state = Switched(2, 1, [400, 800, 1600, 0, 0], "PRO X2 SUPERSTRIKE");
        Assert.Equal("Profile 2 · 800 DPI", TrayText.SwitchNotification(state, []).Text);
    }

    [Fact]
    public void SwitchNotification_without_device_name_titles_ProfileHop()
    {
        Assert.Equal("ProfileHop", TrayText.SwitchNotification(Switched(2, 1, null, null), Names).Title);
    }

    [Fact]
    public void SwitchNotification_leaves_out_a_zero_dpi_slot()
    {
        var state = Switched(2, 3, [400, 800, 1600, 0, 0], null);
        Assert.Null(state.CurrentDpi);
        Assert.Equal("Game (profile 2)", TrayText.SwitchNotification(state, Names).Text);
    }

    [Fact]
    public void Unnamed_profile()
    {
        var status = WorkerStatus.Reachable(new ProfileState(1, 4, [1, 2, 4], null));
        Assert.Equal("ProfileHop: profile 4", TrayText.Tooltip(status, Names));
    }

    [Fact]
    public void Unreachable()
    {
        Assert.Equal("ProfileHop: Mouse not reachable", TrayText.Tooltip(WorkerStatus.Unreachable("boom"), Names));
    }

    [Fact]
    public void Starting()
    {
        Assert.Equal("ProfileHop: connecting…", TrayText.Tooltip(WorkerStatus.Starting, Names));
    }

    [Fact]
    public void Host_mode_is_flagged()
    {
        var status = WorkerStatus.Reachable(new ProfileState(2, 1, [1, 2], null));
        Assert.Equal("ProfileHop: mouse in host mode (close G HUB)", TrayText.Tooltip(status, Names));
    }

    [Fact]
    public void Host_mode_icon_glyph_is_H_not_profile_0()
    {
        Assert.Equal("H", TrayText.IconGlyph(WorkerStatus.Reachable(new ProfileState(2, 0, [1], 4))));
        Assert.Equal("H", TrayText.IconGlyph(WorkerStatus.Reachable(new ProfileState(1, 0, [1], 4))));
        Assert.Equal("2", TrayText.IconGlyph(WorkerStatus.Reachable(new ProfileState(1, 2, [1, 2], 4))));
    }

    [Fact]
    public void Missing_chosen_device_is_named_with_a_hint()
    {
        var status = WorkerStatus.Unreachable("not found") with { MissingDevice = "PRO X2 SUPERSTRIKE" };
        Assert.Equal("ProfileHop: PRO X2 SUPERSTRIKE not found (asleep or off? move it)", TrayText.Tooltip(status, Names));
        Assert.Equal("?", TrayText.IconGlyph(status));
    }

    private static readonly KnownDevice Mouse = new("C54D/PRO X2 SUPERSTRIKE", "PRO X2 SUPERSTRIKE (receiver C54D)", 3, true);
    private static readonly KnownDevice Keyboard = new("C54D/G&KEYS", "G&KEYS (receiver C54D)", 0, false);

    [Fact]
    public void Device_menu_lists_devices_marks_in_use_and_checks_preference()
    {
        var items = TrayText.DeviceMenu([Mouse, Keyboard], "c54d/g&keys");

        Assert.Equal(
        [
            new DeviceMenuItem("PRO X2 SUPERSTRIKE (receiver C54D)  (in use)", "C54D/PRO X2 SUPERSTRIKE", false),
            new DeviceMenuItem("G&&KEYS (receiver C54D)", "C54D/G&KEYS", true),
        ], items);
    }

    [Fact]
    public void Device_menu_automatic_checks_nothing()
    {
        Assert.All(TrayText.DeviceMenu([Mouse, Keyboard], null), i => Assert.False(i.Checked));
        Assert.Empty(TrayText.DeviceMenu(null, null));
    }

    [Fact]
    public void Device_menu_keeps_missing_preference_as_checked_item()
    {
        var items = TrayText.DeviceMenu([Mouse], "C547/G502 X PLUS");

        Assert.Equal(new DeviceMenuItem("C547/G502 X PLUS (not found)", "C547/G502 X PLUS", true), items[^1]);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Tooltip_is_truncated_to_shell_limit()
    {
        IReadOnlyList<ProfileName> names = [new(1, new string('x', 300))];
        var status = WorkerStatus.Reachable(new ProfileState(1, 1, [1], null));
        var tip = TrayText.Tooltip(status, names);
        Assert.Equal(TrayText.MaxTooltipLength, tip.Length);
        Assert.EndsWith("…", tip);
    }

    [Fact]
    public void Menu_label()
    {
        Assert.Equal("1  Normal", TrayText.MenuLabel(1, Names));
        Assert.Equal("3  Profile 3", TrayText.MenuLabel(3, Names));
    }

    [Fact]
    public void Menu_label_escapes_ampersand()
    {
        Assert.Equal("1  R&&D", TrayText.MenuLabel(1, [new(1, "R&D")]));
    }

    [Fact]
    public void Icon_glyph()
    {
        Assert.Equal("2", TrayText.IconGlyph(WorkerStatus.Reachable(new ProfileState(1, 2, [1, 2], null))));
        Assert.Equal("?", TrayText.IconGlyph(WorkerStatus.Unreachable("x")));
        Assert.Equal("…", TrayText.IconGlyph(WorkerStatus.Starting));
    }
}
