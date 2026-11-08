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
public sealed record ConfigLoadResult(AppConfig Config, IReadOnlyList<string> Errors, bool CreatedDefaults);

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

    /// <summary>Loads config; never throws. Missing file → defaults written. Broken file → defaults, error, file kept.</summary>
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
            return new ConfigLoadResult(defaults, errors, CreatedDefaults: true);
        }

        AppConfig config;
        try
        {
            var json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            errors.Add($"{path} is not valid, using defaults: {ex.Message}");
            return new ConfigLoadResult(new AppConfig(), errors, CreatedDefaults: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not read {path}, using defaults: {ex.Message}");
            return new ConfigLoadResult(new AppConfig(), errors, CreatedDefaults: false);
        }

        Normalize(config, errors);
        return new ConfigLoadResult(config, errors, CreatedDefaults: false);
    }

    public static void Save(string path, AppConfig config)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, WriteContext.AppConfig));
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

        config.Hotkeys = config.Hotkeys.Where(h => h is not null).ToList();
    }
}
