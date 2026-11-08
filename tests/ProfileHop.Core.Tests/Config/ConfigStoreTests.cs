using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Config;

public class ConfigStoreTests
{
    [Fact]
    public void Defaults_match_plan()
    {
        var c = new AppConfig();
        Assert.Equal([new ProfileName(1, "Normal"), new ProfileName(2, "Game")], c.Profiles);
        Assert.Null(c.Cycle);
        Assert.Equal(
            [
                new HotkeyBinding("Ctrl+Alt+F9", "next-profile"),
                new HotkeyBinding("Ctrl+Alt+F10", "profile 1"),
                new HotkeyBinding("Ctrl+Alt+F11", "profile 2"),
                new HotkeyBinding("Ctrl+Alt+F12", "next-dpi-slot"),
            ],
            c.Hotkeys);
        Assert.False(c.ForceOnboardMode);
        Assert.False(c.Sound);
        Assert.False(c.Balloon);
        Assert.Equal(LogLevel.Info, c.LogLevel);
    }

    [Fact]
    public void First_run_writes_defaults()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", "config.json");

        var result = ConfigStore.Load(path);

        Assert.True(result.CreatedDefaults);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"profiles\"", json);
        Assert.Contains("\"Ctrl+Alt+F9\"", json);
        Assert.Contains("\"logLevel\": \"Info\"", json);

        var again = ConfigStore.Load(path);
        Assert.False(again.CreatedDefaults);
        Assert.Empty(again.Errors);
        Assert.Equal(result.Config.Profiles, again.Config.Profiles);
        Assert.Equal(result.Config.Hotkeys, again.Config.Hotkeys);
    }

    [Fact]
    public void Reads_user_values_case_insensitive_with_comments()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """
            {
              // my setup
              "Profiles": [ { "number": 1, "name": "Desk" }, { "number": 3, "name": "Aim" } ],
              "cycle": [1, 3],
              "hotkeys": [ { "keys": "Ctrl+Shift+F1", "action": "profile 3" } ],
              "forceOnboardMode": true,
              "sound": true,
              "balloon": true,
              "logLevel": "debug",
            }
            """);

        var r = ConfigStore.Load(path);

        Assert.Empty(r.Errors);
        Assert.Equal([new ProfileName(1, "Desk"), new ProfileName(3, "Aim")], r.Config.Profiles);
        Assert.Equal([1, 3], r.Config.Cycle);
        Assert.Equal([new HotkeyBinding("Ctrl+Shift+F1", "profile 3")], r.Config.Hotkeys);
        Assert.True(r.Config.ForceOnboardMode);
        Assert.True(r.Config.Sound);
        Assert.True(r.Config.Balloon);
        Assert.Equal(LogLevel.Debug, r.Config.LogLevel);
    }

    [Fact]
    public void Missing_fields_keep_defaults()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "sound": true }""");

        var r = ConfigStore.Load(path);

        Assert.Empty(r.Errors);
        Assert.True(r.Config.Sound);
        Assert.Equal(new AppConfig().Profiles, r.Config.Profiles);
        Assert.Equal(new AppConfig().Hotkeys, r.Config.Hotkeys);
    }

    [Fact]
    public void Empty_hotkey_list_disables_all_hotkeys()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "hotkeys": [] }""");

        Assert.Empty(ConfigStore.Load(path).Config.Hotkeys);
    }

    [Fact]
    public void Null_lists_fall_back_to_defaults()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "profiles": null, "hotkeys": null }""");

        var r = ConfigStore.Load(path);

        Assert.Equal(new AppConfig().Profiles, r.Config.Profiles);
        Assert.Equal(new AppConfig().Hotkeys, r.Config.Hotkeys);
    }

    [Fact]
    public void Invalid_json_keeps_defaults_and_reports_error_without_overwriting()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, "{ not json");

        var r = ConfigStore.Load(path);

        Assert.False(r.CreatedDefaults);
        var error = Assert.Single(r.Errors);
        Assert.Contains("config.json", error);
        Assert.Equal(new AppConfig().Hotkeys, r.Config.Hotkeys);
        Assert.Equal("{ not json", File.ReadAllText(path)); // user's file is left alone
    }

    [Fact]
    public void Wrong_types_are_reported_not_thrown()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "sound": "loud", "logLevel": "Chatty" }""");

        var r = ConfigStore.Load(path);

        Assert.Single(r.Errors);
        Assert.False(r.Config.Sound);
    }

    [Fact]
    public void Invalid_profile_numbers_are_dropped_and_reported()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """
            { "profiles": [ { "number": 0, "name": "Zero" }, { "number": 2, "name": "Game" }, { "number": 2, "name": "Dup" } ],
              "cycle": [ 1, -1, 2 ] }
            """);

        var r = ConfigStore.Load(path);

        Assert.Equal([new ProfileName(2, "Game")], r.Config.Profiles);
        Assert.Equal([1, 2], r.Config.Cycle);
        Assert.Equal(3, r.Errors.Count);
    }

    [Fact]
    public void Null_profile_name_becomes_empty()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "profiles": [ { "number": 4 } ] }""");

        Assert.Equal([new ProfileName(4, "")], ConfigStore.Load(path).Config.Profiles);
    }

    [Fact]
    public void Device_defaults_to_automatic()
    {
        Assert.Null(new AppConfig().Device);
    }

    [Fact]
    public void Save_round_trips()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var c = new AppConfig { Sound = true, Cycle = [2, 1], LogLevel = LogLevel.Warning, Device = "receiver:046d:c547" };

        ConfigStore.Save(path, c);
        var r = ConfigStore.Load(path);

        Assert.Empty(r.Errors);
        Assert.Contains("\"device\": \"receiver:046d:c547\"", File.ReadAllText(path));
        Assert.Equal("receiver:046d:c547", r.Config.Device);
        Assert.True(r.Config.Sound);
        Assert.Equal([2, 1], r.Config.Cycle);
        Assert.Equal(LogLevel.Warning, r.Config.LogLevel);
    }

    [Fact]
    public void Unwritable_location_reports_error_and_returns_defaults()
    {
        using var dir = new TempDir();
        var blocker = dir.File("blocker");
        File.WriteAllText(blocker, "a file, not a folder");
        var path = Path.Combine(blocker, "config.json");

        var r = ConfigStore.Load(path);

        Assert.Single(r.Errors);
        Assert.Equal(new AppConfig().Hotkeys, r.Config.Hotkeys);
    }
}
