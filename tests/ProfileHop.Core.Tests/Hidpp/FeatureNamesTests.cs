using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tests.Hidpp;

public class FeatureNamesTests
{
    [Theory]
    [InlineData(0x0000, "ROOT")]
    [InlineData(0x0001, "FEATURE_SET")]
    [InlineData(0x0005, "DEVICE_NAME")]
    [InlineData(0x8100, "ONBOARD_PROFILES")]
    [InlineData(0x2201, "ADJUSTABLE_DPI")]
    public void Known_ids_have_solaar_names(ushort id, string name)
    {
        Assert.Equal(name, FeatureNames.Get(id));
    }

    [Fact]
    public void Unknown_id_returns_null()
    {
        Assert.Null(FeatureNames.Get(0xABCD));
    }
}
