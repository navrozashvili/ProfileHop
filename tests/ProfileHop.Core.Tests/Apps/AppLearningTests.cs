using ProfileHop.Core.Apps;
using ProfileHop.Core.Config;

namespace ProfileHop.Core.Tests.Apps;

public class AppLearningTests
{
    private const string Game = @"c:\games\cs2\cs2.exe";

    [Fact]
    public void NewEntry_IsStored_ReturnsTrue()
    {
        var config = new AppConfig();
        Assert.True(AppLearning.Record(config, Game, 3));
        Assert.Equal(3, config.AppProfiles[Game]);
    }

    [Fact]
    public void SameValue_ReturnsFalse()
    {
        var config = new AppConfig();
        config.AppProfiles[Game] = 3;
        Assert.False(AppLearning.Record(config, Game, 3));
        Assert.Equal(3, config.AppProfiles[Game]);
        Assert.Single(config.AppProfiles);
    }

    [Fact]
    public void DifferentValue_IsReplaced_ReturnsTrue()
    {
        var config = new AppConfig();
        config.AppProfiles[Game] = 3;
        Assert.True(AppLearning.Record(config, Game, 1));
        Assert.Equal(1, config.AppProfiles[Game]);
    }

    [Fact]
    public void Key_IsNormalised()
    {
        var config = new AppConfig();
        Assert.True(AppLearning.Record(config, @"  C:\Games\CS2\CS2.EXE ", 2));
        Assert.Equal(Game, Assert.Single(config.AppProfiles).Key);
        Assert.False(AppLearning.Record(config, @"C:\GAMES\cs2\cs2.exe", 2));
    }

    [Fact]
    public void WorksOnCaseSensitiveDictionary()
    {
        // A config built by hand (not through ConfigStore) may hold an ordinal dictionary; the key is still normalised.
        var config = new AppConfig { AppProfiles = new Dictionary<string, int>() };
        Assert.True(AppLearning.Record(config, @"C:\Games\CS2\cs2.exe", 2));
        Assert.True(config.AppProfiles.ContainsKey(Game));
    }
}
