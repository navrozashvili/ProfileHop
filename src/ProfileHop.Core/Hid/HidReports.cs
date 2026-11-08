namespace ProfileHop.Core.Hid;

/// <summary>Which HID++ report a collection carries.</summary>
public enum HidReportKind
{
    Ignore,
    Short,
    Long,
}

/// <summary>Pure helpers for HID++ collections and output buffers.</summary>
public static class HidReports
{
    public const ushort VendorUsagePage = 0xFF00;
    public const ushort ShortUsage = 0x0001;
    public const ushort LongUsage = 0x0002;

    /// <summary>Usage page 0xFF00, usage 1 = short (ID 0x10), usage 2 = long (ID 0x11); anything else is ignored.</summary>
    public static HidReportKind Classify(ushort usagePage, ushort usage) =>
        (usagePage, usage) switch
        {
            (VendorUsagePage, ShortUsage) => HidReportKind.Short,
            (VendorUsagePage, LongUsage) => HidReportKind.Long,
            _ => HidReportKind.Ignore,
        };

    /// <summary>Copies <paramref name="report"/> into a zero-padded buffer of exactly <paramref name="length"/> bytes.</summary>
    public static byte[] PadToLength(ReadOnlySpan<byte> report, int length)
    {
        if (report.IsEmpty)
        {
            throw new ArgumentException("Report is empty.", nameof(report));
        }

        if (report.Length > length)
        {
            throw new ArgumentException(
                $"Report is {report.Length} bytes, the collection accepts {length}.", nameof(report));
        }

        byte[] buffer = new byte[length];
        report.CopyTo(buffer);
        return buffer;
    }

    /// <summary>Index of the collection to write to: the long collection if present, else the first with an output report; -1 if none.</summary>
    public static int ChooseWriteTarget(IReadOnlyList<HidCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        int fallback = -1;
        for (int i = 0; i < collections.Count; i++)
        {
            HidCollection c = collections[i];
            if (c.OutputReportLength <= 0)
            {
                continue;
            }

            if (Classify(c.UsagePage, c.Usage) == HidReportKind.Long)
            {
                return i;
            }

            if (fallback < 0)
            {
                fallback = i;
            }
        }

        return fallback;
    }
}
