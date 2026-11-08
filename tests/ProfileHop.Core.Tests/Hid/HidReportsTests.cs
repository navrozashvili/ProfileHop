using ProfileHop.Core.Hid;

namespace ProfileHop.Core.Tests.Hid;

public class HidReportsTests
{
    [Theory]
    [InlineData(0xFF00, 0x0001, HidReportKind.Short)]
    [InlineData(0xFF00, 0x0002, HidReportKind.Long)]
    [InlineData(0xFF00, 0x0004, HidReportKind.Ignore)]
    [InlineData(0x0001, 0x0002, HidReportKind.Ignore)]
    [InlineData(0xFF43, 0x0202, HidReportKind.Ignore)]
    public void Classify(int usagePage, int usage, HidReportKind expected)
    {
        Assert.Equal(expected, HidReports.Classify((ushort)usagePage, (ushort)usage));
    }

    [Fact]
    public void PadToLength_PadsWithZeros()
    {
        byte[] buffer = HidReports.PadToLength([0x11, 0xFF, 0x00, 0x1B], 20);

        Assert.Equal(20, buffer.Length);
        Assert.Equal([0x11, 0xFF, 0x00, 0x1B], buffer[..4]);
        Assert.All(buffer[4..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void PadToLength_RejectsTooLongReport()
    {
        Assert.Throws<ArgumentException>(() => HidReports.PadToLength(new byte[21], 20));
    }

    [Fact]
    public void PadToLength_RejectsEmptyReport()
    {
        Assert.Throws<ArgumentException>(() => HidReports.PadToLength([], 20));
    }

    [Fact]
    public void ChooseWriteTarget_PrefersLongCollection()
    {
        HidCollection shortCol = HidPathsTests.Make("a&col01", 1, 7, 7);
        HidCollection longCol = HidPathsTests.Make("a&col02", 2, 20, 20);

        Assert.Equal(1, HidReports.ChooseWriteTarget([shortCol, longCol]));
    }

    [Fact]
    public void ChooseWriteTarget_FallsBackToFirstWritable()
    {
        HidCollection noOutput = HidPathsTests.Make("a&col01", 1, 7, 0);
        HidCollection shortCol = HidPathsTests.Make("a&col02", 1, 7, 7);

        Assert.Equal(1, HidReports.ChooseWriteTarget([noOutput, shortCol]));
    }

    [Fact]
    public void ChooseWriteTarget_NoneWritable_ReturnsMinusOne()
    {
        Assert.Equal(-1, HidReports.ChooseWriteTarget([HidPathsTests.Make("a", 1, 7, 0)]));
    }
}
