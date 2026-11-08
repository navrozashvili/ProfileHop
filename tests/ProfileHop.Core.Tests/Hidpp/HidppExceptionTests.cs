using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tests.Hidpp;

public class HidppExceptionTests
{
    [Theory]
    [InlineData(1, HidppError.Unknown)]
    [InlineData(2, HidppError.InvalidArgument)]
    [InlineData(3, HidppError.OutOfRange)]
    [InlineData(4, HidppError.HwError)]
    [InlineData(5, HidppError.LogitechInternal)]
    [InlineData(6, HidppError.InvalidFeatureIndex)]
    [InlineData(7, HidppError.InvalidFunctionId)]
    [InlineData(8, HidppError.Busy)]
    [InlineData(9, HidppError.Unsupported)]
    public void Error_codes_match_hidpp20_values(int code, HidppError expected)
    {
        Assert.Equal(expected, (HidppError)code);
    }

    [Fact]
    public void HidppException_carries_code_and_request_context()
    {
        var ex = new HidppException(HidppError.InvalidArgument, deviceIndex: 0x01, featureIndex: 0x09, function: 3);

        Assert.Equal(HidppError.InvalidArgument, ex.Error);
        Assert.Equal(0x01, ex.DeviceIndex);
        Assert.Equal(0x09, ex.FeatureIndex);
        Assert.Equal(3, ex.Function);
        Assert.Contains("InvalidArgument", ex.Message);
    }

    [Fact]
    public void Unknown_code_still_formats()
    {
        var ex = new HidppException((HidppError)0x42, 1, 2, 0);
        Assert.Contains("0x42", ex.Message);
    }

    [Fact]
    public void DeviceUnreachableException_carries_hidpp10_code()
    {
        var ex = new DeviceUnreachableException(deviceIndex: 0x02, hidpp10Error: 0x09);

        Assert.Equal(0x02, ex.DeviceIndex);
        Assert.Equal(0x09, ex.Hidpp10Error);
        Assert.Contains("0x02", ex.Message);
    }
}
