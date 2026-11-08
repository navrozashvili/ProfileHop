namespace ProfileHop.Core.Hid;

/// <summary>Pure helpers for HID device interface paths.</summary>
public static class HidPaths
{
    /// <summary>
    /// Key shared by all top-level collections of one device, lower-cased: the path without its <c>&amp;colNN</c>
    /// segment, with the instance ID cut before its last <c>&amp;</c> (that part is the collection number) and without
    /// the interface class GUID. <c>\\?\hid#vid_046d&amp;pid_c54d&amp;mi_02&amp;col01#a&amp;6df8f16&amp;0&amp;0000#{guid}</c>
    /// → <c>\\?\hid#vid_046d&amp;pid_c54d&amp;mi_02#a&amp;6df8f16&amp;0</c>. The instance ID keeps two receivers of the
    /// same model apart. A path without a collection suffix is its own device.
    /// </summary>
    public static string DeviceKey(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string lower = path.ToLowerInvariant();
        int col = FindCollection(lower);
        if (col < 0)
        {
            return lower;
        }

        string hardwareId = lower[..col];
        int instanceStart = lower.IndexOf('#', col);
        if (instanceStart < 0)
        {
            return hardwareId;
        }

        int instanceEnd = lower.IndexOf('#', instanceStart + 1);
        string instance = instanceEnd < 0 ? lower[instanceStart..] : lower[instanceStart..instanceEnd];
        int lastAmp = instance.LastIndexOf('&');
        return hardwareId + (lastAmp > 0 ? instance[..lastAmp] : instance);
    }

    /// <summary>Index of the <c>&amp;colNN</c> segment, or -1.</summary>
    private static int FindCollection(string lower)
    {
        int start = 0;
        while (true)
        {
            int i = lower.IndexOf("&col", start, StringComparison.Ordinal);
            if (i < 0)
            {
                return -1;
            }

            if (i + 6 <= lower.Length && char.IsAsciiHexDigit(lower[i + 4]) && char.IsAsciiHexDigit(lower[i + 5]))
            {
                return i;
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
