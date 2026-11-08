using ProfileHop.Core.Config;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Config;

public class HotkeyPlanTests
{
    [Theory]
    [InlineData("next-profile", HotkeyActionKind.NextProfile, 0)]
    [InlineData("Next-Profile", HotkeyActionKind.NextProfile, 0)]
    [InlineData("next-dpi-slot", HotkeyActionKind.NextDpiSlot, 0)]
    [InlineData("profile 2", HotkeyActionKind.Profile, 2)]
    [InlineData("  PROFILE   5 ", HotkeyActionKind.Profile, 5)]
    public void Parses_actions(string text, HotkeyActionKind kind, int profile)
    {
        Assert.True(HotkeyAction.TryParse(text, out var action, out _));
        Assert.Equal(new HotkeyAction(kind, profile), action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("profile")]
    [InlineData("profile 0")]
    [InlineData("profile x")]
    [InlineData("dance")]
    public void Rejects_bad_actions(string text)
    {
        Assert.False(HotkeyAction.TryParse(text, out _, out var error));
        Assert.Contains("next-profile", error);
    }

    [Fact]
    public void Builds_registrations_from_defaults()
    {
        var plan = HotkeyPlan.Build(new AppConfig().Hotkeys);

        Assert.Empty(plan.Errors);
        Assert.Equal(4, plan.Entries.Count);
        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x78), plan.Entries[0].Hotkey);
        Assert.Equal(new HotkeyAction(HotkeyActionKind.NextProfile, 0), plan.Entries[0].Action);
        Assert.Equal(new HotkeyAction(HotkeyActionKind.Profile, 2), plan.Entries[2].Action);
        Assert.Equal([1, 2, 3, 4], plan.Entries.Select(e => e.Id));
        Assert.Equal("Ctrl+Alt+F12", plan.Entries[3].Text);
    }

    [Fact]
    public void Skips_disabled_and_reports_invalid_and_duplicate()
    {
        var plan = HotkeyPlan.Build(
        [
            new HotkeyBinding("", "next-profile"),            // disabled
            new HotkeyBinding("Ctrl+Alt+F99", "profile 1"),   // bad key
            new HotkeyBinding("Ctrl+Alt+F10", "jump"),        // bad action
            new HotkeyBinding("Ctrl+Alt+F11", "profile 2"),
            new HotkeyBinding("alt+ctrl+f11", "profile 1"),   // duplicate of the previous one
        ]);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("Ctrl+Alt+F11", entry.Text);
        Assert.Equal(3, plan.Errors.Count);
        Assert.Contains(plan.Errors, e => e.Contains("F99", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("jump", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("more than once", StringComparison.Ordinal));
    }
}
