using System.Text.Json;
using System.Text.Json.Serialization;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Config;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

/// <param name="Config">Always usable: defaults when the file was missing or broken.</param>
/// <param name="Errors">Problems to show the user; empty when everything was fine.</param>
/// <param name="CreatedDefaults">True when the file did not exist and defaults were written.</param>
public sealed record ConfigLoadResult(AppConfig Config, IReadOnlyList<string> Errors, bool CreatedDefaults)
{
    /// <summary>
    /// True when the file exists but could not be read or parsed (<see cref="Config"/> is then defaults): it must not
    /// be overwritten by an automatic save.
    /// </summary>
    public bool Broken { get; init; }

    /// <summary>The file's last-write time (UTC) as loaded or written, or null when there is no file.</summary>
    public DateTime? LastWriteUtc { get; init; }
}

public static class ConfigStore
{
    // Relaxed escaping keeps "Ctrl+Alt+F9" readable instead of "Ctrl+Alt+F9" in the hand-edited file.
    private static readonly ConfigJsonContext WriteContext = new(new JsonSerializerOptions(ConfigJsonContext.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProfileHop");

    public static string DefaultPath { get; } = Path.Combine(DefaultDirectory, "config.json");

    /// <summary>Loads config; never throws. Missing file → defaults written. Broken file → defaults, error, file kept, <see cref="ConfigLoadResult.Broken"/>.</summary>
    public static ConfigLoadResult Load(string path)
    {
        var errors = new List<string>();
        if (!File.Exists(path))
        {
            var defaults = new AppConfig();
            try
            {
                Save(path, defaults);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Could not write default config to {path}: {ex.Message}");
                return new ConfigLoadResult(defaults, errors, CreatedDefaults: false);
            }
            return new ConfigLoadResult(defaults, errors, CreatedDefaults: true) { LastWriteUtc = LastWriteUtc(path) };
        }

        // Taken before reading: an edit that lands while reading then shows up as a change.
        var lastWrite = LastWriteUtc(path);
        AppConfig config;
        try
        {
            var json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            errors.Add($"{path} is not valid: {ex.Message}");
            return new ConfigLoadResult(new AppConfig(), errors, CreatedDefaults: false) { Broken = true, LastWriteUtc = lastWrite };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not read {path}: {ex.Message}");
            return new ConfigLoadResult(new AppConfig(), errors, CreatedDefaults: false) { Broken = true, LastWriteUtc = lastWrite };
        }

        Normalize(config, errors);
        return new ConfigLoadResult(config, errors, CreatedDefaults: false) { LastWriteUtc = lastWrite };
    }

    /// <summary>The file's last-write time (UTC), or null when it does not exist or can't be queried.</summary>
    public static DateTime? LastWriteUtc(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <c>config.json.tmp</c> next to <paramref name="path"/>, flushes it to disk, then swaps it in with
    /// File.Replace, keeping the previous file as <c>config.json.bak</c>. A failure at any point leaves the old file
    /// as it was.
    /// </summary>
    public static void Save(string path, AppConfig config) => Save(path, config, (stream, bytes) => stream.Write(bytes));

    /// <summary><paramref name="write"/> puts the bytes into the temp file (a seam for tests).</summary>
    internal static void Save(string path, AppConfig config, Action<FileStream, byte[]> write)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(config, WriteContext.AppConfig);
        var tmp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream, bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, path);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void Normalize(AppConfig config, List<string> errors)
    {
        config.Profiles ??= AppConfig.DefaultProfiles();
        config.Hotkeys ??= AppConfig.DefaultHotkeys();

        var seen = new HashSet<int>();
        var profiles = new List<ProfileName>(config.Profiles.Count);
        foreach (var p in config.Profiles)
        {
            if (p is null) continue;
            if (p.Number < 1)
                errors.Add($"Profile number {p.Number} is invalid (profiles start at 1); ignored.");
            else if (!seen.Add(p.Number))
                errors.Add($"Profile {p.Number} is listed more than once; the first name is used.");
            else
                profiles.Add(p.Name is null ? p with { Name = "" } : p);
        }
        config.Profiles = profiles;

        if (config.Cycle is not null)
        {
            var bad = config.Cycle.Where(n => n < 1).ToList();
            if (bad.Count > 0)
            {
                errors.Add($"Cycle entries {string.Join(", ", bad)} are invalid (profiles start at 1); ignored.");
                config.Cycle = config.Cycle.Where(n => n >= 1).ToList();
            }
        }

        if (config.DefaultProfile is < 1)
        {
            errors.Add($"defaultProfile {config.DefaultProfile} is invalid (profiles start at 1); ignored.");
            config.DefaultProfile = null;
        }

        var apps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, profile) in config.AppProfiles ?? [])
        {
            var key = path?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key))
                errors.Add($"appProfiles has a blank path (profile {profile}); ignored.");
            else if (profile < 1)
                errors.Add($"appProfiles: {key} maps to profile {profile}, which is invalid (profiles start at 1); ignored.");
            else
                apps[key] = profile; // paths that differ only in case collapse; the last one wins
        }
        config.AppProfiles = apps;

        config.Hotkeys = config.Hotkeys.Where(h => h is not null).ToList();
    }
}
