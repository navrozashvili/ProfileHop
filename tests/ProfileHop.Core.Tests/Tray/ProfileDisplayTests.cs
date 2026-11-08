using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class ProfileDisplayTests
{
    private static readonly Dictionary<int, string> MouseNames = new() { [1] = "400", [5] = "3200" };

    private static ProfileState Mouse(params int[] enabled) => new(1, enabled[0], enabled, 0) { ProfileNames = MouseNames };

    [Fact]
    public void Name_precedence_is_config_then_mouse_then_none()
    {
        IReadOnlyList<ProfileName> config = [new(5, "Aim"), new(1, " ")];

        Assert.Equal("Aim", ProfileDisplay.NameOf(5, config, MouseNames));
        Assert.Equal("400", ProfileDisplay.NameOf(1, config, MouseNames)); // blank config name falls through
        Assert.Null(ProfileDisplay.NameOf(3, config, MouseNames));
        Assert.Equal("Aim", ProfileDisplay.NameOf(5, config, null));
    }

    [Fact]
    public void Label_falls_back_to_Profile_N()
    {
        Assert.Equal("3200", ProfileDisplay.Label(5, [], MouseNames));
        Assert.Equal("Profile 3", ProfileDisplay.Label(3, [], MouseNames));
    }

    [Fact]
    public void Available_is_the_mouse_enabled_profiles()
    {
        var a = ProfileDisplay.Available(Mouse(1, 4, 5), new AppConfig());

        Assert.Equal([1, 4, 5], a.Profiles);
        Assert.True(a.FromMouse);
    }

    [Fact]
    public void Available_with_one_enabled_profile_adds_the_configured_profiles()
    {
        var a = ProfileDisplay.Available(Mouse(1), new AppConfig()); // default hotkeys: profiles 1-5

        Assert.Equal([1, 2, 3, 4, 5], a.Profiles);
        Assert.True(a.FromMouse);
    }

    [Fact]
    public void Available_without_state_uses_config_hotkey_profiles_or_1_to_5()
    {
        var fromHotkeys = ProfileDisplay.Available(null, new AppConfig { Hotkeys = [new("Ctrl+Alt+7", "profile 7"), new("", "profile 2")] });
        Assert.Equal([2, 7], fromHotkeys.Profiles);
        Assert.False(fromHotkeys.FromMouse);

        Assert.Equal([1, 2, 3, 4, 5], ProfileDisplay.Available(null, new AppConfig { Hotkeys = [] }).Profiles);
    }

    [Fact]
    public void Configured_numbers_are_config_profiles_and_profile_hotkeys()
    {
        var config = new AppConfig
        {
            Profiles = [new(3, "Aim")],
            Hotkeys = [new("Ctrl+Alt+F9", "next-profile"), new("Ctrl+Alt+1", "profile 1"), new("", "profile 6"), new("x", "bogus")],
        };

        Assert.Equal([1, 3, 6], ProfileDisplay.ConfiguredNumbers(config));
    }
}
