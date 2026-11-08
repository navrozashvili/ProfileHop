using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class ProfileCycleTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 1)] // wraps
    public void Next_moves_through_enabled_profiles(int current, int expected)
    {
        Assert.Equal(expected, ProfileCycle.Next(current, [1, 2, 3], cycleOverride: null));
    }

    [Fact]
    public void Next_skips_disabled_profiles()
    {
        Assert.Equal(4, ProfileCycle.Next(2, [1, 2, 4], cycleOverride: null));
    }

    [Fact]
    public void Current_not_in_list_goes_to_first()
    {
        Assert.Equal(1, ProfileCycle.Next(5, [1, 2, 3], cycleOverride: null));
    }

    [Fact]
    public void Override_replaces_enabled_list()
    {
        Assert.Equal(1, ProfileCycle.Next(2, [1, 2, 3, 4, 5], cycleOverride: [1, 2]));
        Assert.Equal(1, ProfileCycle.Next(3, [1, 2, 3], cycleOverride: [1, 2]));
        Assert.Equal(5, ProfileCycle.Next(1, [1, 2, 3], cycleOverride: [1, 5]));
    }

    [Fact]
    public void Empty_override_is_ignored()
    {
        Assert.Equal(3, ProfileCycle.Next(2, [1, 2, 3], cycleOverride: []));
    }

    [Fact]
    public void No_candidates_keeps_current()
    {
        Assert.Equal(2, ProfileCycle.Next(2, [], cycleOverride: null));
    }

    [Fact]
    public void Single_candidate_stays()
    {
        Assert.Equal(1, ProfileCycle.Next(1, [1], cycleOverride: null));
    }
}
