using System.Collections.Frozen;

namespace ProfileHop.Core.Apps;

/// <summary>Pure rules for deciding which foreground windows count as "an app" for per-app profiles.</summary>
public static class AppRules
{
    // Taken from Windows 11. Extend by adding entries; nothing else needs to change.
    private static readonly FrozenSet<string> ShellClasses = new[]
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "ForegroundStaging", "TaskListThumbnailWnd",
        "#32768",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ShellExes = new[]
    {
        "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe", "ShellExperienceHost.exe", "LockApp.exe",
        "ShellHost.exe",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalised identity of an app: its trimmed, lower-cased image path.</summary>
    public static string Key(string imagePath) => imagePath.Trim().ToLowerInvariant();

    /// <summary>True for windows that must not count as an app coming to the front (taskbar, Start, popup menus...).</summary>
    public static bool IsShellSurface(string? windowClass, string? exeFileName)
        => (windowClass is not null && ShellClasses.Contains(windowClass))
           || (exeFileName is not null && ShellExes.Contains(exeFileName));

    /// <summary>True for the UWP frame host, whose real app lives in a child window.</summary>
    public static bool IsFrameHost(string exeFileName)
        => string.Equals(exeFileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>The profile for <paramref name="appKey"/>: its stored entry, otherwise <paramref name="defaultProfile"/>.</summary>
    public static int? Target(string appKey, IReadOnlyDictionary<string, int> appProfiles, int? defaultProfile)
        => appProfiles.TryGetValue(appKey, out var profile) ? profile : defaultProfile;
}
