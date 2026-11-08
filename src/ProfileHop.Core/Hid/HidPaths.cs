namespace ProfileHop.Core.Hid;

/// <summary>Pure helpers for HID device interface paths.</summary>
public static class HidPaths
{
    /// <summary>
    /// Key shared by all top-level collections of one device: the lower-cased path prefix before
    /// <c>&amp;colNN</c>. A path without a collection suffix is its own device.
    /// </summary>
    public static string DeviceKey(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string lower = path.ToLowerInvariant();
        int start = 0;
        while (true)
        {
            int i = lower.IndexOf("&col", start, StringComparison.Ordinal);
            if (i < 0)
            {
                return lower;
            }

            if (i + 6 <= lower.Length && char.IsAsciiHexDigit(lower[i + 4]) && char.IsAsciiHexDigit(lower[i + 5]))
            {
                return lower[..i];
            }

            start = i + 4;
        }
    }

    /// <summary>Groups collections into devices by <see cref="DeviceKey"/>, keeping first-seen order.</summary>
    public static IReadOnlyList<HidDevice> Group(IEnumerable<HidCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        var order = new List<string>();
        var groups = new Dictionary<string, List<HidCollection>>(StringComparer.Ordinal);
        foreach (HidCollection c in collections)
        {
            string key = DeviceKey(c.Path);
            if (!groups.TryGetValue(key, out List<HidCollection>? list))
            {
                list = [];
                groups.Add(key, list);
                order.Add(key);
            }

            list.Add(c);
        }

        return order.Select(k => new HidDevice(k, groups[k])).ToList();
    }
}
