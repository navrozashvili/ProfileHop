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

    /// <summary>
    /// Why a device can't be used, or null. Every request is sent as a long (0x11) report, and the HID++ 2.0 calls we
    /// use need long replies (memoryRead returns 16 bytes), so a device with only the short collection is skipped.
    /// </summary>
    public static string? Unusable(IReadOnlyList<HidCollection> collections) =>
        ChooseWriteTarget(collections) < 0 ? "no long HID++ collection" : null;

    /// <summary>Index of the writable long collection (usage 2, with an output report); -1 if there is none.</summary>
    public static int ChooseWriteTarget(IReadOnlyList<HidCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        for (int i = 0; i < collections.Count; i++)
        {
            HidCollection c = collections[i];
            if (c.OutputReportLength > 0 && Classify(c.UsagePage, c.Usage) == HidReportKind.Long)
            {
                return i;
            }
        }

        return -1;
    }
}
