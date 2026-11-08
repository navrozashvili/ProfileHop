namespace ProfileHop.Core.Hidpp;

/// <summary>Header fields of one onboard profile sector (diagnostics only).</summary>
/// <param name="ReportRateMs">Byte 0: report interval in ms (libratbag converts with <c>1000 / max(1, rate)</c>).</param>
/// <param name="DefaultDpiIndex">Byte 1.</param>
/// <param name="SwitchedDpiIndex">Byte 2 (DPI-shift / sniper slot).</param>
/// <param name="Dpi">Bytes 3..12: five little-endian u16 DPI values; 0 = disabled slot.</param>
/// <param name="StoredCrc">Last two sector bytes, big-endian.</param>
/// <param name="ComputedCrc">CRC-CCITT (seed 0xFFFF) over all bytes but the last two.</param>
public sealed record ProfileSettings(
    byte ReportRateMs,
    byte DefaultDpiIndex,
    byte SwitchedDpiIndex,
    IReadOnlyList<ushort> Dpi,
    ushort StoredCrc,
    ushort ComputedCrc)
{
    public int ReportRateHz => 1000 / Math.Max((int)ReportRateMs, 1);
    public bool CrcValid => StoredCrc == ComputedCrc;
    public IReadOnlyList<bool> SlotEnabled => Dpi.Select(d => d != 0).ToArray();
}

/// <summary>
/// One profile sector in profile format 0x08 (PRO X2 SUPERSTRIKE). Layout <b>inferred from device dumps, not from
/// libratbag/Solaar</b> (neither knows format 0x08); see docs/protocol-verification.md.
/// </summary>
/// <param name="ReportRateCode">Byte 0, raw: values 03/04/05 seen, mapping to Hz unknown.</param>
/// <param name="Byte1">Byte 1, raw, meaning unknown (0x03 in every dump).</param>
/// <param name="DefaultDpiIndex">Byte 2, 0-based; equals getCurrentDpiIndex right after switching to the profile.</param>
/// <param name="ShiftDpiIndex">Byte 3, 0-based (assumed DPI-shift slot; equal to byte 2 in every dump).</param>
/// <param name="Dpi">X DPI of the five 5-byte entries at 4..28 (<c>[X LE16, Y LE16, flag]</c>); 0 = disabled.</param>
/// <param name="DpiY">Y DPI of each entry.</param>
/// <param name="DpiFlags">Fifth byte of each entry, raw.</param>
/// <param name="HitsCandidate">Bytes 0x26..0x2B, raw: two 3-byte groups that differ between profiles with a different
/// HITS click feel; probably per-button (left/right) HITS settings. Not interpreted.</param>
/// <param name="Name">UTF-16LE at 0xA0 up to NUL (or 0xFFFF); null when empty or the sector is too short.</param>
public sealed record Format08Profile(
    byte ReportRateCode, byte Byte1, byte DefaultDpiIndex, byte ShiftDpiIndex,
    IReadOnlyList<ushort> Dpi, IReadOnlyList<ushort> DpiY, IReadOnlyList<byte> DpiFlags,
    IReadOnlyList<byte> HitsCandidate, string? Name, ushort StoredCrc, ushort ComputedCrc)
{
    public bool CrcValid => StoredCrc == ComputedCrc;
    public IReadOnlyList<bool> SlotEnabled => Dpi.Select(d => d != 0).ToArray();
}

/// <summary>
/// Decodes the header of a profile sector read with memoryRead. libratbag (<c>union hidpp20_internal_profile</c>,
/// hidpp20.c:1816-1842, parsed hidpp20.c:2746-2755) and Solaar (<c>OnboardProfile.from_bytes</c>, hidpp20.py:1550-1556)
/// use one layout for every profile format id they accept (1..5: libratbag hidpp20.c:1802-1806, 2224-2228; Solaar
/// rejects <c>profile &gt; 0x05</c>, hidpp20.py:1666). See docs/protocol-verification.md.
/// </summary>
public static class ProfileSector
{
    private const int HeaderLength = 13;

    /// <summary>libratbag <c>HIDPP20_ONBOARD_PROFILES_PROFILE_TYPE_*</c> name for a profile format id, or null.</summary>
    public static string? FormatName(byte format) => format switch
    {
        1 => "G402",
        2 => "G303",
        3 => "G900",
        4 => "G915",
        5 => "G502X",
        _ => null,
    };

    public static bool CanDecode(byte format) => FormatName(format) is not null;

    /// <summary>Profile format reported by the PRO X2 SUPERSTRIKE; layout inferred from its dumps.</summary>
    public const byte Format08 = 0x08;

    private const int Format08EntriesOffset = 4;
    private const int Format08EntrySize = 5;
    private const int Format08HeadLength = Format08EntriesOffset + Format08EntrySize * OnboardProfilesFeature.DpiSlotCount; // 29
    private const int Format08HitsOffset = 0x26;
    private const int Format08HitsLength = 6;
    private const int Format08NameOffset = 0xA0;

    public static bool IsInferredFormat08(byte format) => format == Format08;

    /// <summary>
    /// Bytes at the start of a profile sector needed to read its five DPI values for <paramref name="format"/>
    /// (13 for libratbag formats 1..5, 29 for 0x08), or 0 when the layout is unknown.
    /// </summary>
    public static int DpiHeadLength(byte format) =>
        CanDecode(format) ? HeaderLength : IsInferredFormat08(format) ? Format08HeadLength : 0;

    /// <summary>
    /// The five DPI values (0 = disabled slot, indexes as used by get/setCurrentDpiIndex) from the start of a profile
    /// sector, or null when the format is unknown or <paramref name="head"/> is shorter than <see cref="DpiHeadLength"/>.
    /// </summary>
    public static IReadOnlyList<ushort>? DpiSlots(byte format, ReadOnlySpan<byte> head)
    {
        int need = DpiHeadLength(format);
        if (need == 0 || head.Length < need)
            return null;
        var dpi = new ushort[OnboardProfilesFeature.DpiSlotCount];
        for (int i = 0; i < dpi.Length; i++)
        {
            int at = IsInferredFormat08(format) ? Format08EntriesOffset + Format08EntrySize * i : 3 + 2 * i;
            dpi[i] = (ushort)(head[at] | (head[at + 1] << 8));
        }
        return dpi;
    }

    /// <summary>
    /// Decodes a format 0x08 sector (inferred layout, see <see cref="Format08Profile"/>), or null when it is shorter than
    /// the HITS-candidate bytes. The name and CRC need the whole sector.
    /// </summary>
    public static Format08Profile? DecodeFormat08(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < Format08HitsOffset + Format08HitsLength)
            return null;
        const int n = OnboardProfilesFeature.DpiSlotCount;
        var x = new ushort[n];
        var y = new ushort[n];
        var flags = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int at = Format08EntriesOffset + Format08EntrySize * i;
            x[i] = (ushort)(sector[at] | (sector[at + 1] << 8));
            y[i] = (ushort)(sector[at + 2] | (sector[at + 3] << 8));
            flags[i] = sector[at + 4];
        }
        byte[] hits = sector.Slice(Format08HitsOffset, Format08HitsLength).ToArray();
        ushort stored = (ushort)((sector[^2] << 8) | sector[^1]);
        return new Format08Profile(sector[0], sector[1], sector[2], sector[3], x, y, flags, hits,
            Utf16Name(sector, Format08NameOffset), stored, CrcCcitt(sector[..^2]));
    }

    private static string? Utf16Name(ReadOnlySpan<byte> sector, int offset)
    {
        var name = new System.Text.StringBuilder();
        for (int at = offset; at + 1 < sector.Length - 2; at += 2)
        {
            char c = (char)(sector[at] | (sector[at + 1] << 8));
            if (c is '\0' or '￿')
                break;
            name.Append(c);
        }
        return name.Length == 0 ? null : name.ToString();
    }

    /// <summary>Header fields, or null when the format id is unknown or the sector is shorter than the header.</summary>
    public static ProfileSettings? Decode(byte format, ReadOnlySpan<byte> sector)
    {
        if (!CanDecode(format) || sector.Length < HeaderLength)
            return null;
        var dpi = new ushort[OnboardProfilesFeature.DpiSlotCount];
        for (int i = 0; i < dpi.Length; i++)
            dpi[i] = (ushort)(sector[3 + 2 * i] | (sector[4 + 2 * i] << 8));
        ushort stored = (ushort)((sector[^2] << 8) | sector[^1]);
        return new ProfileSettings(sector[0], sector[1], sector[2], dpi, stored, CrcCcitt(sector[..^2]));
    }

    /// <summary>
    /// Last two bytes = big-endian CRC-CCITT of the rest (libratbag <c>hidpp20_onboard_profiles_is_sector_valid</c>,
    /// hidpp20.c:1921-1934). libratbag ignores a directory or profile whose CRC is wrong (hidpp20.c:2739-2743, 2841).
    /// </summary>
    public static bool HasValidCrc(ReadOnlySpan<byte> sector) =>
        sector.Length > 2 && ((sector[^2] << 8) | sector[^1]) == CrcCcitt(sector[..^2]);

    /// <summary>Every byte 0xFF (never written).</summary>
    public static bool IsErased(ReadOnlySpan<byte> sector) => !sector.IsEmpty && !sector.ContainsAnyExcept((byte)0xFF);

    /// <summary>libratbag <c>hidpp_crc_ccitt</c> (hidpp-generic.c:385-404), seed 0xFFFF.</summary>
    public static ushort CrcCcitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            ushort temp = (ushort)((crc >> 8) ^ b);
            crc <<= 8;
            ushort quick = (ushort)(temp ^ (temp >> 4));
            crc ^= quick;
            quick <<= 5;
            crc ^= quick;
            quick <<= 7;
            crc ^= quick;
        }
        return crc;
    }
}
