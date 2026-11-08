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

    [Fact]
    public void Fewer_than_two_enabled_falls_back_to_configured_profiles()
    {
        Assert.Equal(2, ProfileCycle.Next(1, [1], cycleOverride: null, configured: [1, 2]));
        Assert.Equal(1, ProfileCycle.Next(2, [1], cycleOverride: null, configured: [1, 2]));
        Assert.Equal(1, ProfileCycle.Next(0, [], cycleOverride: null, configured: [1, 2]));
    }

    [Fact]
    public void Configured_profiles_are_not_used_with_two_or_more_enabled()
    {
        Assert.Equal(3, ProfileCycle.Next(2, [1, 2, 3], cycleOverride: null, configured: [1, 2]));
    }

    [Fact]
    public void Override_wins_over_configured_fallback()
    {
        Assert.Equal(3, ProfileCycle.Next(1, [1], cycleOverride: [1, 3], configured: [1, 2]));
    }

    [Fact]
    public void Candidates_reports_when_the_configured_fallback_is_used()
    {
        Assert.True(ProfileCycle.Candidates([1], null, [1, 2]).ConfiguredFallback);
        Assert.False(ProfileCycle.Candidates([1, 2], null, [1, 2]).ConfiguredFallback);
        Assert.False(ProfileCycle.Candidates([1], [1, 2], [1, 2]).ConfiguredFallback);
        Assert.False(ProfileCycle.Candidates([1], null, []).ConfiguredFallback);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 3)] // wraps from first to last
    public void Previous_moves_back_through_enabled_profiles(int current, int expected)
    {
        Assert.Equal(expected, ProfileCycle.Previous(current, [1, 2, 3], cycleOverride: null));
    }

    [Fact]
    public void Previous_skips_disabled_profiles()
    {
        Assert.Equal(2, ProfileCycle.Previous(4, [1, 2, 4], cycleOverride: null));
    }

    [Fact]
    public void Previous_from_current_not_in_list_goes_to_last()
    {
        Assert.Equal(3, ProfileCycle.Previous(5, [1, 2, 3], cycleOverride: null));
    }

    [Fact]
    public void Previous_uses_the_override()
    {
        Assert.Equal(5, ProfileCycle.Previous(1, [1, 2, 3], cycleOverride: [1, 5]));
        Assert.Equal(5, ProfileCycle.Previous(3, [1, 2, 3], cycleOverride: [1, 5]));
    }

    [Fact]
    public void Previous_with_no_candidates_or_one_keeps_current()
    {
        Assert.Equal(2, ProfileCycle.Previous(2, [], cycleOverride: null));
        Assert.Equal(1, ProfileCycle.Previous(1, [1], cycleOverride: null));
    }

    [Fact]
    public void Previous_with_fewer_than_two_enabled_falls_back_to_configured_profiles()
    {
        Assert.Equal(3, ProfileCycle.Previous(1, [1], cycleOverride: null, configured: [3, 1, 2]));
        Assert.Equal(1, ProfileCycle.Previous(2, [1], cycleOverride: null, configured: [1, 2]));
        Assert.Equal(2, ProfileCycle.Previous(0, [], cycleOverride: null, configured: [1, 2]));
        Assert.Equal(1, ProfileCycle.Previous(2, [1, 2, 3], cycleOverride: null, configured: [1, 5]));
    }
}
