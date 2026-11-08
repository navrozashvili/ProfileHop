namespace ProfileHop.Core.Tray;

/// <summary>String values under HKCU\Software\Microsoft\Windows\CurrentVersion\Run (behind an interface for tests).</summary>
public interface IRunKeyStore
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>"Start with Windows": the Run value <c>ProfileHop</c> holding the quoted exe path.</summary>
public sealed class Autostart(IRunKeyStore store, string exePath)
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ProfileHop";

    private string Command => "\"" + exePath + "\"";

    public bool IsEnabled => !string.IsNullOrEmpty(store.Get(ValueName));

    public void Enable() => store.Set(ValueName, Command);

    public void Disable() => store.Delete(ValueName);

    /// <summary>Flips the setting; returns the new state.</summary>
    public bool Toggle()
    {
        if (IsEnabled) Disable();
        else Enable();
        return IsEnabled;
    }

    /// <summary>If autostart is on but points at another exe (app moved), point it here. Returns true if it changed.</summary>
    public bool RepairPath()
    {
        var current = store.Get(ValueName);
        if (string.IsNullOrEmpty(current) || string.Equals(current, Command, StringComparison.OrdinalIgnoreCase)) return false;
        Enable();
        return true;
    }
}
