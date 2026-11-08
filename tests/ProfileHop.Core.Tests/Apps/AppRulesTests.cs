using ProfileHop.Core.Apps;

namespace ProfileHop.Core.Tests.Apps;

public class AppRulesTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("MultitaskingViewFrame")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("ForegroundStaging")]
    [InlineData("TaskListThumbnailWnd")]
    [InlineData("#32768")]
    public void ShellSurface_ByClass(string cls) => Assert.True(AppRules.IsShellSurface(cls, "explorer.exe"));

    [Theory]
    [InlineData("StartMenuExperienceHost.exe")]
    [InlineData("SearchHost.exe")]
    [InlineData("SearchApp.exe")]
    [InlineData("ShellExperienceHost.exe")]
    [InlineData("LockApp.exe")]
    [InlineData("ShellHost.exe")]
    [InlineData("searchhost.EXE")]
    public void ShellSurface_ByExe(string exe) => Assert.True(AppRules.IsShellSurface("Windows.UI.Core.CoreWindow", exe));

    [Theory]
    [InlineData("Progman", "explorer.exe")]
    [InlineData("WorkerW", "explorer.exe")]
    [InlineData("Chrome_WidgetWin_1", "chrome.exe")]
    [InlineData("UnrealWindow", "game.exe")]
    [InlineData(null, null)]
    public void NotShellSurface(string? cls, string? exe) => Assert.False(AppRules.IsShellSurface(cls, exe));

    [Fact]
    public void Key_TrimsAndLowercases() => Assert.Equal("c:\\games\\x.exe", AppRules.Key(" C:\\Games\\X.exe "));

    [Fact]
    public void Target_StoredBeatsDefault()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["app-a"] = 3 };
        Assert.Equal(3, AppRules.Target("app-a", map, 1));
    }

    [Fact]
    public void Target_MissingGivesDefault()
        => Assert.Equal(1, AppRules.Target("app-b", new Dictionary<string, int>(), 1));

    [Fact]
    public void Target_MissingAndNoDefaultIsNull()
        => Assert.Null(AppRules.Target("app-b", new Dictionary<string, int>(), null));

    [Fact]
    public void IsFrameHost_CaseInsensitive()
    {
        Assert.True(AppRules.IsFrameHost("applicationframehost.exe"));
        Assert.False(AppRules.IsFrameHost("chrome.exe"));
    }
}
