using ProfileHop.Core.Hidpp;
using ProfileHop.Core.Tests.Hidpp.Fakes;

namespace ProfileHop.Core.Tests.Hidpp;

/// <summary>Profile format 0x08 (PRO X2 SUPERSTRIKE): layout inferred from real device dumps, not libratbag/Solaar.</summary>
public class ProfileSectorFormat08Tests
{
    private static byte[] Real(ushort sector) => Format08Fixture.Sector(sector);

    [Fact]
    public void Format08_is_the_inferred_layout_not_a_libratbag_one()
    {
        Assert.True(ProfileSector.IsInferredFormat08(0x08));
        Assert.False(ProfileSector.IsInferredFormat08(0x05));
        Assert.False(ProfileSector.CanDecode(0x08));
        Assert.Null(ProfileSector.Decode(0x08, Real(1)));
    }

    [Fact]
    public void Format08_fixture_crcs_are_valid()
    {
        foreach (ushort s in new ushort[] { 1, 2, 3, 4, 5, 0x0101 })
            Assert.True(ProfileSector.HasValidCrc(Real(s)));
    }

    [Fact]
    public void Format08_profile1_has_only_400_enabled()
    {
        Format08Profile p = ProfileSector.DecodeFormat08(Real(1))!;

        Assert.Equal(0x05, p.ReportRateCode);
        Assert.Equal(0x03, p.Byte1);
        Assert.Equal(0, p.DefaultDpiIndex);
        Assert.Equal(0, p.ShiftDpiIndex);
        Assert.Equal([400, 0, 0, 0, 0], p.Dpi);
        Assert.Equal([true, false, false, false, false], p.SlotEnabled);
        Assert.Equal("400", p.Name);
        Assert.Equal([0x08, 0x09, 0x0C, 0x08, 0x09, 0x0C], p.HitsCandidate);
        Assert.True(p.CrcValid);
    }

    [Fact]
    public void Format08_entries_are_X_Y_LE16_plus_flag_byte()
    {
        Format08Profile p = ProfileSector.DecodeFormat08(Real(2))!;

        Assert.Equal(1, p.DefaultDpiIndex);
        Assert.Equal(1, p.ShiftDpiIndex);
        Assert.Equal([600, 600, 600, 600, 600], p.Dpi);
        Assert.Equal([600, 600, 600, 600, 600], p.DpiY);
        Assert.Equal([0x01, 0x02, 0x02, 0x02, 0x02], p.DpiFlags);
        Assert.Equal("600", p.Name);
    }

    [Fact]
    public void Format08_profile4_fifth_entry_is_disabled()
    {
        Format08Profile p = ProfileSector.DecodeFormat08(Real(4))!;

        Assert.Equal(0x04, p.ReportRateCode);
        Assert.Equal(3, p.DefaultDpiIndex);
        Assert.Equal(3, p.ShiftDpiIndex);
        Assert.Equal([1600, 1600, 1600, 1600, 0], p.Dpi);
        Assert.Equal([true, true, true, true, false], p.SlotEnabled);
        Assert.Equal("1600", p.Name);
    }

    [Fact]
    public void Format08_profile5_has_different_hits_candidate_bytes()
    {
        Format08Profile p = ProfileSector.DecodeFormat08(Real(5))!;

        Assert.Equal(4, p.DefaultDpiIndex);
        Assert.Equal([3200, 3200, 3200, 3200, 3200], p.Dpi);
        Assert.Equal([0x20, 0x09, 0x0C, 0x20, 0x09, 0x0C], p.HitsCandidate);
        Assert.Equal("3200", p.Name);
    }

    [Fact]
    public void Format08_rom_profile_lists_five_distinct_dpis_and_no_name()
    {
        Format08Profile p = ProfileSector.DecodeFormat08(Real(0x0101))!;

        Assert.Equal(0x03, p.ReportRateCode);
        Assert.Equal([800, 1200, 1600, 2400, 3200], p.Dpi);
        Assert.Equal([0x14, 0x08, 0x0C, 0x14, 0x08, 0x0C], p.HitsCandidate);
        Assert.Null(p.Name);
    }

    [Fact]
    public void Format08_crc_mismatch_is_reported()
    {
        byte[] data = Real(1);
        data[4] ^= 0x01;

        Format08Profile p = ProfileSector.DecodeFormat08(data)!;
        Assert.False(p.CrcValid);
        Assert.NotEqual(p.StoredCrc, p.ComputedCrc);
    }

    [Fact]
    public void Format08_sector_shorter_than_the_hits_bytes_is_null()
    {
        Assert.Null(ProfileSector.DecodeFormat08(new byte[0x2B]));
    }

    [Theory]
    [InlineData(0x0004, new ushort[] { 1600, 1600, 1600, 1600, 0 })]
    [InlineData(0x0001, new ushort[] { 400, 0, 0, 0, 0 })]
    [InlineData(0x0101, new ushort[] { 800, 1200, 1600, 2400, 3200 })]
    public void DpiSlots_uses_the_inferred_layout_for_format08(ushort sector, ushort[] expected)
    {
        Assert.Equal(expected, ProfileSector.DpiSlots(0x08, Real(sector)));
        Assert.Equal(expected, ProfileSector.DpiSlots(0x08, Real(sector).AsSpan(0, 32))); // two memoryRead chunks
    }

    [Fact]
    public void DpiSlots_uses_libratbag_layout_for_1_to_5_and_null_otherwise()
    {
        byte[] lib = new byte[16];
        lib[3] = 0x90; lib[4] = 0x01; // dpi[0] = 400
        Assert.Equal([400, 0, 0, 0, 0], ProfileSector.DpiSlots(2, lib));
        Assert.Null(ProfileSector.DpiSlots(6, lib));
        Assert.Null(ProfileSector.DpiSlots(0x08, lib)); // too short for the five 0x08 entries
    }

    [Theory]
    [InlineData(1, 13)]
    [InlineData(5, 13)]
    [InlineData(0x08, 29)]
    [InlineData(6, 0)]
    public void DpiHeadLength_is_the_bytes_needed_for_the_dpi_list(byte format, int length)
    {
        Assert.Equal(length, ProfileSector.DpiHeadLength(format));
    }
}
