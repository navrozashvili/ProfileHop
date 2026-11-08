namespace ProfileHop.Core.Tray;

public static class ProfileCycle
{
    /// <summary>
    /// Next profile after <paramref name="current"/> among <see cref="Candidates"/>. Wraps around; a current profile not
    /// in the list goes to the first. No candidates → stays on current.
    /// </summary>
    public static int Next(int current, IReadOnlyList<int> enabled, IReadOnlyList<int>? cycleOverride,
        IReadOnlyList<int>? configured = null)
    {
        var list = Candidates(enabled, cycleOverride, configured).List;
        if (list.Count == 0) return current;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == current) return list[(i + 1) % list.Count];
        }
        return list[0];
    }

    /// <summary>
    /// Profiles "next" cycles through: <paramref name="cycleOverride"/> if non-empty; else the device's enabled profiles
    /// if there are at least two; else the <paramref name="configured"/> profile numbers (config <c>profiles</c>,
    /// ascending) if there are at least two — the real mouse can report a single enabled profile in its directory
    /// although more are usable. <c>ConfiguredFallback</c> is true when that last case applies.
    /// </summary>
    public static (IReadOnlyList<int> List, bool ConfiguredFallback) Candidates(IReadOnlyList<int> enabled,
        IReadOnlyList<int>? cycleOverride, IReadOnlyList<int>? configured)
    {
        if (cycleOverride is { Count: > 0 }) return (cycleOverride, false);
        if (enabled.Count >= 2 || configured is null) return (enabled, false);
        int[] fallback = [.. configured.Where(n => n > 0).Distinct().Order()];
        return fallback.Length >= 2 ? (fallback, true) : (enabled, false);
    }
}
