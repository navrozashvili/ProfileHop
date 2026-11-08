namespace ProfileHop.Core.Config;

public enum AutoSaveVerdict
{
    /// <summary>Safe to save.</summary>
    Save,

    /// <summary>The file could not be read or parsed at load: saving would replace it with defaults.</summary>
    Broken,

    /// <summary>The file changed on disk since it was last loaded or saved (a hand edit not yet reloaded).</summary>
    ChangedOnDisk,
}

/// <summary>
/// Decides whether an automatic save (a learned per-app profile, the captured default) may write config.json, so it
/// never overwrites a broken file or a hand edit the app has not reloaded. Explicit user saves skip the check and call
/// <see cref="Saved"/>. UI-thread only.
/// </summary>
public sealed class ConfigSaveGuard(string path)
{
    private DateTime? _lastWriteUtc;
    private bool _broken;

    /// <summary>After a load (startup or Reload config). A broken load blocks automatic saves until a good load or a save.</summary>
    public void Loaded(ConfigLoadResult result)
    {
        _broken = result.Broken;
        if (!result.Broken) _lastWriteUtc = result.LastWriteUtc;
    }

    /// <summary>After a successful save: this is now the file on disk.</summary>
    public void Saved()
    {
        _broken = false;
        _lastWriteUtc = ConfigStore.LastWriteUtc(path);
    }

    public AutoSaveVerdict CheckAutoSave() =>
        _broken ? AutoSaveVerdict.Broken
        : ConfigStore.LastWriteUtc(path) != _lastWriteUtc ? AutoSaveVerdict.ChangedOnDisk
        : AutoSaveVerdict.Save;
}
