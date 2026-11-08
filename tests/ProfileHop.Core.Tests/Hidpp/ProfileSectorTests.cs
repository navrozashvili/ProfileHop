using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tests.Hidpp;

public class ProfileSectorTests
{
    /// <summary>255-byte sector (the real mouse's size) with the libratbag/Solaar header and a valid CRC.</summary>
    private static byte[] Sector(byte reportRate, byte defaultIndex, byte switchedIndex, params ushort[] dpi)
    {
        var data = new byte[255];
        Array.Fill(data, (byte)0xFF);
        data[0] = reportRate;
        data[1] = defaultIndex;
        data[2] = switchedIndex;
        for (int i = 0; i < 5; i++)
        {
            data[3 + 2 * i] = (byte)dpi[i];
            data[4 + 2 * i] = (byte)(dpi[i] >> 8);
        }
        ushort crc = ProfileSector.CrcCcitt(data.AsSpan(0, data.Length - 2));
        data[^2] = (byte)(crc >> 8);
        data[^1] = (byte)crc;
        return data;
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(0xFF, false)]
    public void Formats_1_to_5_share_the_libratbag_and_Solaar_layout(byte format, bool decodable)
    {
        Assert.Equal(decodable, ProfileSector.CanDecode(format));
    }

    [Fact]
    public void Layout_names_follow_libratbag_constants()
    {
        Assert.Equal("G402", ProfileSector.FormatName(1));
        Assert.Equal("G303", ProfileSector.FormatName(2));
        Assert.Equal("G900", ProfileSector.FormatName(3));
        Assert.Equal("G915", ProfileSector.FormatName(4));
        Assert.Equal("G502X", ProfileSector.FormatName(5));
        Assert.Null(ProfileSector.FormatName(6));
    }

    [Fact]
    public void Decode_reads_report_rate_dpi_indexes_and_five_le16_dpi_values()
    {
        byte[] data = Sector(1, 2, 3, 400, 800, 1600, 0x1F40, 0);

        ProfileSettings? p = ProfileSector.Decode(5, data);

        Assert.NotNull(p);
        Assert.Equal(1, p.ReportRateMs);
        Assert.Equal(1000, p.ReportRateHz);
        Assert.Equal(2, p.DefaultDpiIndex);
        Assert.Equal(3, p.SwitchedDpiIndex);
        Assert.Equal([400, 800, 1600, 8000, 0], p.Dpi);
        Assert.Equal([true, true, true, true, false], p.SlotEnabled);
    }

    [Fact]
    public void Report_rate_byte_is_milliseconds_like_libratbag()
    {
        Assert.Equal(125, ProfileSector.Decode(1, Sector(8, 0, 0, 800, 0, 0, 0, 0))!.ReportRateHz);
        Assert.Equal(1000, ProfileSector.Decode(1, Sector(0, 0, 0, 800, 0, 0, 0, 0))!.ReportRateHz); // libratbag max(1, rate)
    }

    [Fact]
    public void Decode_returns_null_for_unknown_format_or_short_sector()
    {
        Assert.Null(ProfileSector.Decode(6, Sector(1, 0, 0, 800, 0, 0, 0, 0)));
        Assert.Null(ProfileSector.Decode(1, new byte[12]));
    }

    [Fact]
    public void Crc_ccitt_matches_libratbag_seed_FFFF()
    {
        // CRC-16/CCITT-FALSE check value
        Assert.Equal(0x29B1, ProfileSector.CrcCcitt("123456789"u8));
    }

    [Fact]
    public void Crc_is_checked_against_the_last_two_bytes_big_endian()
    {
        byte[] data = Sector(1, 0, 0, 800, 0, 0, 0, 0);
        Assert.True(ProfileSector.Decode(1, data)!.CrcValid);

        data[5] ^= 0x01;
        ProfileSettings p = ProfileSector.Decode(1, data)!;
        Assert.False(p.CrcValid);
        Assert.NotEqual(p.StoredCrc, p.ComputedCrc);
    }

    [Fact]
    public void HasValidCrc_works_for_any_sector()
    {
        byte[] data = Sector(1, 0, 0, 800, 0, 0, 0, 0);
        Assert.True(ProfileSector.HasValidCrc(data));
        data[0] ^= 0x80;
        Assert.False(ProfileSector.HasValidCrc(data));
        Assert.False(ProfileSector.HasValidCrc([0x12]));
    }

    [Fact]
    public void Erased_sector_is_reported()
    {
        var data = new byte[255];
        Array.Fill(data, (byte)0xFF);

        Assert.True(ProfileSector.IsErased(data));
        Assert.False(ProfileSector.IsErased(Sector(1, 0, 0, 800, 0, 0, 0, 0)));
    }
}
