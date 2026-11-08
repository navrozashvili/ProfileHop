using ProfileHop.Core.Config;

namespace ProfileHop.Core.Apps;

/// <summary>Remembers the profile the user chose for an app (per-app profiles).</summary>
public static class AppLearning
{
    /// <summary>
    /// Stores <paramref name="profile"/> for <paramref name="appKey"/> (normalised with <see cref="AppRules.Key"/>) in
    /// <see cref="AppConfig.AppProfiles"/>. Returns true when the stored value changed, i.e. the config needs saving.
    /// </summary>
    public static bool Record(AppConfig config, string appKey, int profile)
    {
        var key = AppRules.Key(appKey);
        if (config.AppProfiles.TryGetValue(key, out var stored) && stored == profile) return false;
        config.AppProfiles[key] = profile;
        return true;
    }
}
