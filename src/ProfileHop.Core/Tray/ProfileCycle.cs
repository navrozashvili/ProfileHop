namespace ProfileHop.Core.Tray;

public static class ProfileCycle
{
    /// <summary>
    /// Next profile after <paramref name="current"/>. Candidates are <paramref name="cycleOverride"/> when it is non-empty,
    /// otherwise the device's enabled profiles. Wraps around; a current profile not in the list goes to the first.
    /// No candidates → stays on current.
    /// </summary>
    public static int Next(int current, IReadOnlyList<int> enabled, IReadOnlyList<int>? cycleOverride)
    {
        var list = cycleOverride is { Count: > 0 } ? cycleOverride : enabled;
        if (list.Count == 0) return current;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == current) return list[(i + 1) % list.Count];
        }
        return list[0];
    }
}
