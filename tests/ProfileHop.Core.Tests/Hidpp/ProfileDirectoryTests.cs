using ProfileHop.Core.Hidpp;
using ProfileHop.Core.Tests.Hidpp.Fakes;

namespace ProfileHop.Core.Tests.Hidpp;

public class ProfileDirectoryTests
{
    private readonly List<int> _offsets = [];

    private Func<int, byte[]> Reader(byte[] sector) => offset =>
    {
        _offsets.Add(offset);
        return sector[offset..(offset + 16)];
    };

    private static byte[] Raw(params byte[] head)
    {
        var data = new byte[256];
        Array.Fill(data, (byte)0xFF);
        head.CopyTo(data, 0);
        return data;
    }

    [Fact]
    public void Parses_entries_until_FF_FF_from_one_chunk()
    {
        byte[] sector = Raw(0x00, 0x01, 0x01, 0x00, 0x00, 0x02, 0x01, 0x00, 0xFF, 0xFF, 0x00, 0x00);

        var entries = ProfileDirectory.Parse(Reader(sector), sectorSize: 255, maxEntries: 5);

        Assert.Equal([new(1, 0x0001, true), new(2, 0x0002, true)], entries);
        Assert.Equal([0], _offsets);
    }

    [Fact]
    public void Enabled_profiles_skip_disabled_entries()
    {
        byte[] sector = SimulatedDevice.Directory((1, true), (2, false), (3, true));

        var entries = ProfileDirectory.Parse(Reader(sector), 255, 5);

        Assert.Equal([1, 3], ProfileDirectory.EnabledProfiles(entries));
    }

    [Fact]
    public void Any_non_zero_enabled_byte_counts_as_enabled()
    {
        byte[] sector = Raw(0x00, 0x01, 0x02, 0x00, 0x00, 0x02, 0x80, 0x00, 0xFF, 0xFF);

        var entries = ProfileDirectory.Parse(Reader(sector), 255, 5);

        Assert.Equal([1, 2], ProfileDirectory.EnabledProfiles(entries));
    }

    [Fact]
    public void Profile_number_is_the_sector_value()
    {
        byte[] sector = SimulatedDevice.Directory((2, true), (1, true));

        var entries = ProfileDirectory.Parse(Reader(sector), 255, 5);

        Assert.Equal([1, 2], ProfileDirectory.EnabledProfiles(entries)); // ascending
    }

    [Fact]
    public void Rom_and_zero_sectors_are_not_user_profiles()
    {
        byte[] sector = Raw(0x00, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x02, 0x01, 0x00, 0xFF, 0xFF);

        var entries = ProfileDirectory.Parse(Reader(sector), 255, 5);

        Assert.Equal(3, entries.Count);
        Assert.Equal([2], ProfileDirectory.EnabledProfiles(entries));
    }

    [Fact]
    public void Immediate_terminator_gives_empty_directory()
    {
        var entries = ProfileDirectory.Parse(Reader(Raw()), 255, 5);

        Assert.Empty(entries);
    }

    [Fact]
    public void Reads_further_chunks_when_entries_cross_16_bytes()
    {
        byte[] sector = SimulatedDevice.Directory((1, true), (2, true), (3, true), (4, true), (5, true));

        var entries = ProfileDirectory.Parse(Reader(sector), 255, 8);

        Assert.Equal([1, 2, 3, 4, 5], ProfileDirectory.EnabledProfiles(entries));
        Assert.Equal([0, 16], _offsets);
    }

    [Fact]
    public void Stops_after_max_entries()
    {
        byte[] sector = SimulatedDevice.Directory((1, true), (2, true), (3, true), (4, true), (5, true));

        var entries = ProfileDirectory.Parse(Reader(sector), 255, maxEntries: 2);

        Assert.Equal(2, entries.Count);
        Assert.Equal([0], _offsets);
    }

    [Fact]
    public void Malformed_directory_without_terminator_is_bounded_to_16_chunks()
    {
        var sector = new byte[256]; // all zero: never FF FF
        sector[2] = 1;

        var entries = ProfileDirectory.Parse(Reader(sector), sectorSize: 0xFFFF, maxEntries: 1000);

        Assert.Equal(16, _offsets.Count);
        Assert.Equal(64, entries.Count);
    }

    [Fact]
    public void Last_chunk_offset_is_clamped_to_sector_size_minus_16()
    {
        // sector size 24: entries 4 and 5 (bytes 16..23) must come from a read at offset 8
        byte[] sector = SimulatedDevice.Directory((1, true), (2, true), (3, true), (4, true), (5, true));

        var entries = ProfileDirectory.Parse(Reader(sector), sectorSize: 24, maxEntries: 10);

        Assert.Equal([0, 8], _offsets);
        Assert.Equal([1, 2, 3, 4, 5], ProfileDirectory.EnabledProfiles(entries));
    }

    [Fact]
    public void Stops_at_end_of_sector_without_terminator()
    {
        byte[] sector = SimulatedDevice.Directory((1, true), (2, true), (3, true), (4, true), (5, true), (6, true));

        var entries = ProfileDirectory.Parse(Reader(sector), sectorSize: 24, maxEntries: 10);

        Assert.Equal(6, entries.Count);
        Assert.Equal([0, 8], _offsets);
    }

    [Fact]
    public void Tiny_sector_size_is_treated_as_one_chunk()
    {
        byte[] sector = SimulatedDevice.Directory((1, true));

        var entries = ProfileDirectory.Parse(Reader(sector), sectorSize: 4, maxEntries: 10);

        Assert.Equal([0], _offsets);
        Assert.Single(entries);
    }
}
