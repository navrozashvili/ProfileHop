using ProfileHop.Core.Config;

namespace ProfileHop.Core.Tests.Config;

public class ConfigSaveGuardTests
{
    private static readonly DateTime Edited = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Automatic_save_is_allowed_after_a_good_load()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var guard = new ConfigSaveGuard(path);

        guard.Loaded(ConfigStore.Load(path));

        Assert.Equal(AutoSaveVerdict.Save, guard.CheckAutoSave());
    }

    [Fact]
    public void Hand_edit_after_load_blocks_automatic_saves_until_reload_or_explicit_save()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var guard = new ConfigSaveGuard(path);
        guard.Loaded(ConfigStore.Load(path));

        File.SetLastWriteTimeUtc(path, Edited);
        Assert.Equal(AutoSaveVerdict.ChangedOnDisk, guard.CheckAutoSave());

        guard.Loaded(ConfigStore.Load(path)); // Reload config
        Assert.Equal(AutoSaveVerdict.Save, guard.CheckAutoSave());

        File.SetLastWriteTimeUtc(path, Edited.AddDays(1));
        ConfigStore.Save(path, new AppConfig()); // an explicit user save
        guard.Saved();
        Assert.Equal(AutoSaveVerdict.Save, guard.CheckAutoSave());
    }

    [Fact]
    public void File_created_or_deleted_after_load_counts_as_changed()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        var guard = new ConfigSaveGuard(path);
        guard.Loaded(ConfigStore.Load(path));

        File.Delete(path);
        Assert.Equal(AutoSaveVerdict.ChangedOnDisk, guard.CheckAutoSave());
    }

    [Fact]
    public void Broken_file_blocks_automatic_saves_until_a_good_reload_or_explicit_save()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, "{ not json");
        var guard = new ConfigSaveGuard(path);

        guard.Loaded(ConfigStore.Load(path));
        Assert.Equal(AutoSaveVerdict.Broken, guard.CheckAutoSave());

        File.WriteAllText(path, "{}");
        guard.Loaded(ConfigStore.Load(path));
        Assert.Equal(AutoSaveVerdict.Save, guard.CheckAutoSave());

        File.WriteAllText(path, "{ not json");
        guard.Loaded(ConfigStore.Load(path));
        ConfigStore.Save(path, new AppConfig());
        guard.Saved();
        Assert.Equal(AutoSaveVerdict.Save, guard.CheckAutoSave());
    }
}
