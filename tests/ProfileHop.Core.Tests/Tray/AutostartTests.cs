using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class AutostartTests
{
    private sealed class FakeRunKey : IRunKeyStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string name) => Values.TryGetValue(name, out var v) ? v : null;

        public void Set(string name, string value) => Values[name] = value;

        public void Delete(string name) => Values.Remove(name);
    }

    private const string Exe = @"C:\Program Files\ProfileHop\profilehop.exe";

    [Fact]
    public void Disabled_when_value_missing()
    {
        Assert.False(new Autostart(new FakeRunKey(), Exe).IsEnabled);
    }

    [Fact]
    public void Enable_writes_quoted_path_under_ProfileHop()
    {
        var key = new FakeRunKey();
        var a = new Autostart(key, Exe);

        a.Enable();

        Assert.Equal("\"" + Exe + "\"", key.Values["ProfileHop"]);
        Assert.True(a.IsEnabled);
        Assert.Equal("ProfileHop", Autostart.ValueName);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", Autostart.RunKeyPath);
    }

    [Fact]
    public void Disable_removes_value()
    {
        var key = new FakeRunKey();
        var a = new Autostart(key, Exe);
        a.Enable();

        a.Disable();

        Assert.Empty(key.Values);
        Assert.False(a.IsEnabled);
    }

    [Fact]
    public void Toggle_flips_and_returns_new_state()
    {
        var a = new Autostart(new FakeRunKey(), Exe);
        Assert.True(a.Toggle());
        Assert.False(a.Toggle());
    }

    [Fact]
    public void Repair_updates_stale_path_only_when_enabled()
    {
        var key = new FakeRunKey();
        var a = new Autostart(key, Exe);

        Assert.False(a.RepairPath()); // not enabled: nothing to do
        Assert.Empty(key.Values);

        key.Values["ProfileHop"] = "\"C:\\old\\profilehop.exe\"";
        Assert.True(a.IsEnabled);
        Assert.True(a.RepairPath());
        Assert.Equal("\"" + Exe + "\"", key.Values["ProfileHop"]);

        Assert.False(a.RepairPath()); // already current
    }

    [Fact]
    public void Repair_treats_path_case_insensitively()
    {
        var key = new FakeRunKey();
        key.Values["ProfileHop"] = "\"" + Exe.ToUpperInvariant() + "\"";
        Assert.False(new Autostart(key, Exe).RepairPath());
    }
}
