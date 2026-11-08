using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tests.Hidpp;

public class HexFormatTests
{
    [Fact]
    public void Frame_is_space_separated_upper_case_hex()
    {
        Assert.Equal("11 01 0E 4B 00 FF", HexFormat.Frame([0x11, 0x01, 0x0E, 0x4B, 0x00, 0xFF]));
        Assert.Equal("", HexFormat.Frame([]));
    }

    [Fact]
    public void Dump_prints_16_bytes_per_line_with_offset()
    {
        byte[] data = Enumerable.Range(0, 35).Select(i => (byte)i).ToArray();

        IReadOnlyList<string> lines = HexFormat.Dump(data);

        Assert.Equal(
        [
            "0000  00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F",
            "0010  10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1D 1E 1F",
            "0020  20 21 22",
        ], lines);
    }
}
