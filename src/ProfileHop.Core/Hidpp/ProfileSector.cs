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
