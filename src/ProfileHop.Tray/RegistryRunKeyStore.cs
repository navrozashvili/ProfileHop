using Microsoft.Win32;
using ProfileHop.Core.Tray;

namespace ProfileHop.Tray;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
internal sealed class RegistryRunKeyStore : IRunKeyStore
{
    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Autostart.RunKeyPath);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Autostart.RunKeyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Autostart.RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
