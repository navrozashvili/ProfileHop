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
        Assert.Equal("ProfileHop: Normal (profile 1), host mode", TrayText.Tooltip(status, Names));
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
