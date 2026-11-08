using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class WindowPlacementTests
{
    [Fact]
    public void Centres_in_the_primary_work_area()
    {
        Assert.Equal((660, 290), WindowPlacement.Center(left: 0, top: 0, right: 1920, bottom: 1040, width: 600, height: 460));
    }

    [Fact]
    public void Centres_on_a_monitor_left_of_the_primary_at_negative_coordinates()
    {
        // Secondary monitor to the left: work area -2560..0. The old Math.Max(0, ...) put the dialog on the primary.
        Assert.Equal((-1580, 490), WindowPlacement.Center(left: -2560, top: 0, right: 0, bottom: 1400, width: 600, height: 420));
    }

    [Fact]
    public void Centres_on_a_monitor_above_with_a_top_taskbar_offset()
    {
        Assert.Equal((960, -800), WindowPlacement.Center(left: 0, top: -1080, right: 2520, bottom: -40, width: 600, height: 480));
    }

    [Fact]
    public void Window_larger_than_the_work_area_keeps_its_top_left_corner_inside()
    {
        Assert.Equal((-1280, 40), WindowPlacement.Center(left: -1280, top: 40, right: 0, bottom: 760, width: 1500, height: 900));
    }
}
