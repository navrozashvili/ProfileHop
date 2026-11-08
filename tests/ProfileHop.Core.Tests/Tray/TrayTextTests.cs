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
    public void Missing_preferred_device_is_flagged()
    {
        var status = WorkerStatus.Reachable(new ProfileState(1, 2, [1, 2], null, PreferredDeviceMissing: true));
        Assert.Equal("ProfileHop: Game (profile 2), chosen device not found", TrayText.Tooltip(status, Names));
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

    [Fact]
    public void Menu_profiles_union_config_and_device()
    {
        // Device-enabled profiles first, then configured names not reported by the device; sorted, distinct.
        Assert.Equal([1, 2, 3], TrayText.MenuProfiles([1, 3], Names));
        Assert.Equal([1, 2], TrayText.MenuProfiles(null, Names));
    }
}
