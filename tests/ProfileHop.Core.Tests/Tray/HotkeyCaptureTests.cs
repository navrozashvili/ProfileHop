using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class HotkeyCaptureTests
{
    private const uint VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12,
        VkEscape = 0x1B, VkSpace = 0x20, VkDelete = 0x2E, VkLWin = 0x5B, VkRWin = 0x5C, VkF1 = 0x70, VkF17 = 0x80,
        VkF23 = 0x86, VkLShift = 0xA0, VkRMenu = 0xA5, VkOemPlus = 0xBB;

    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    private static Hotkey H(string text) => HotkeyParser.Parse(text);

    [Theory]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(24)]
    public void Bare_F13_to_F24_parse_format_and_round_trip(int n)
    {
        var hk = HotkeyParser.Parse("F" + n);
        Assert.Equal(new Hotkey(HotkeyModifiers.None, VkF1 + (uint)(n - 1)), hk);
        Assert.Equal("F" + n, hk.ToString());
        Assert.Equal(hk, HotkeyParser.Parse(hk.ToString()));
    }

    [Fact]
    public void Copilot_key_Win_Shift_F23_parses_in_any_order_and_round_trips()
    {
        var hk = HotkeyParser.Parse("Win+Shift+F23");
        Assert.Equal(new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, VkF23), hk);
        Assert.Equal("Shift+Win+F23", hk.ToString()); // canonical modifier order
        Assert.Equal(hk, HotkeyParser.Parse(hk.ToString()));
    }

    [Fact]
    public void Focus_shows_the_prompt_and_losing_it_restores_the_value()
    {
        var c = new HotkeyCapture(H("Ctrl+Alt+1"));
        Assert.Equal("Ctrl+Alt+1", c.Text);

        c.Begin();
        Assert.True(c.Capturing);
        Assert.Equal(HotkeyCapture.Prompt, c.Text);

        c.End();
        Assert.False(c.Capturing);
        Assert.Equal("Ctrl+Alt+1", c.Text);
        Assert.Equal(H("Ctrl+Alt+1"), c.Value);
    }

    [Fact]
    public void Bare_F17_commits()
    {
        var c = new HotkeyCapture(null);
        c.Begin();

        Assert.Equal(CaptureOutcome.Committed, c.KeyDown(VkF17, HotkeyModifiers.None));
        Assert.Equal(H("F17"), c.Value);
        Assert.Equal("F17", c.Text);
        Assert.False(c.Capturing);
    }

    [Fact]
    public void Copilot_key_sequence_commits_Win_Shift_F23()
    {
        var c = new HotkeyCapture(null);
        c.Begin();

        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkLWin, HotkeyModifiers.Win));
        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkLShift, HotkeyModifiers.Win | HotkeyModifiers.Shift));
        Assert.Equal(CaptureOutcome.Committed, c.KeyDown(VkF23, HotkeyModifiers.Win | HotkeyModifiers.Shift));
        Assert.Equal(H("Win+Shift+F23"), c.Value);
        Assert.Equal("Shift+Win+F23", c.Text);
    }

    [Fact]
    public void Modifier_only_presses_do_not_commit_but_preview()
    {
        var c = new HotkeyCapture(H("F9"));
        c.Begin();

        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkControl, HotkeyModifiers.Control));
        Assert.Equal("Ctrl+…", c.Text);
        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkMenu, CtrlAlt));
        Assert.Equal("Ctrl+Alt+…", c.Text);
        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkRMenu, CtrlAlt));
        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkShift, CtrlAlt | HotkeyModifiers.Shift));
        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkRWin, HotkeyModifiers.Win));
        Assert.Equal(H("F9"), c.Value);
        Assert.True(c.Capturing);

        c.KeyUp(HotkeyModifiers.None);
        Assert.Equal(HotkeyCapture.Prompt, c.Text);
    }

    [Fact]
    public void Ctrl_Alt_digit_commits()
    {
        var c = new HotkeyCapture(null);
        c.Begin();
        c.KeyDown(VkControl, HotkeyModifiers.Control);
        c.KeyDown(VkMenu, CtrlAlt);

        Assert.Equal(CaptureOutcome.Committed, c.KeyDown('1', CtrlAlt));
        Assert.Equal("Ctrl+Alt+1", c.Text);
    }

    [Fact]
    public void Escape_cancels_and_keeps_the_previous_value()
    {
        var c = new HotkeyCapture(H("Ctrl+Alt+1"));
        c.Begin();
        c.KeyDown(VkControl, HotkeyModifiers.Control);

        Assert.Equal(CaptureOutcome.Cancelled, c.KeyDown(VkEscape, HotkeyModifiers.None));
        Assert.Equal(H("Ctrl+Alt+1"), c.Value);
        Assert.Equal("Ctrl+Alt+1", c.Text);
        Assert.False(c.Capturing);
    }

    [Theory]
    [InlineData(VkBack)]
    [InlineData(VkDelete)]
    public void Backspace_or_Delete_without_modifiers_clears(uint vk)
    {
        var c = new HotkeyCapture(H("Ctrl+Alt+1"));
        c.Begin();

        Assert.Equal(CaptureOutcome.Cleared, c.KeyDown(vk, HotkeyModifiers.None));
        Assert.Null(c.Value);
        Assert.Equal("", c.Text);
        Assert.False(c.Capturing);
    }

    [Fact]
    public void Ctrl_Alt_Delete_style_combo_with_modifiers_is_a_key_not_a_clear()
    {
        var c = new HotkeyCapture(null);
        c.Begin();

        Assert.Equal(CaptureOutcome.Committed, c.KeyDown(VkDelete, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.Equal(H("Ctrl+Shift+Delete"), c.Value);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('1')]
    [InlineData(VkSpace)]
    public void Bare_letters_digits_and_space_need_a_modifier(uint vk)
    {
        var c = new HotkeyCapture(H("F9"));
        c.Begin();

        Assert.Equal(CaptureOutcome.Rejected, c.KeyDown(vk, HotkeyModifiers.None));
        Assert.Equal(H("F9"), c.Value);
        Assert.True(c.Capturing);
        Assert.Contains("modifier", c.Text);
    }

    [Fact]
    public void Keys_the_parser_cannot_name_are_rejected()
    {
        var c = new HotkeyCapture(null);
        c.Begin();

        Assert.Equal(CaptureOutcome.Rejected, c.KeyDown(VkOemPlus, CtrlAlt));
        Assert.Null(c.Value);
        Assert.True(c.Capturing);
    }

    [Theory]
    [InlineData(VkTab)]
    [InlineData(VkReturn)]
    public void Tab_and_Enter_without_modifiers_are_left_to_the_dialog(uint vk)
    {
        var c = new HotkeyCapture(H("F9"));
        c.Begin();

        Assert.Equal(CaptureOutcome.NotHandled, c.KeyDown(vk, HotkeyModifiers.None));
        Assert.Equal(H("F9"), c.Value);
        Assert.False(HotkeyCapture.WantsKey(vk, HotkeyModifiers.None));
        Assert.True(HotkeyCapture.WantsKey(vk, HotkeyModifiers.Control));
        Assert.True(HotkeyCapture.WantsKey(VkEscape, HotkeyModifiers.None));
    }

    [Fact]
    public void A_key_after_a_commit_starts_a_new_capture()
    {
        var c = new HotkeyCapture(null);
        c.Begin();
        c.KeyDown(VkF17, HotkeyModifiers.None);

        Assert.Equal(CaptureOutcome.Pending, c.KeyDown(VkControl, HotkeyModifiers.Control));
        Assert.True(c.Capturing);
        Assert.Equal(CaptureOutcome.Committed, c.KeyDown(VkF1, HotkeyModifiers.Control));
        Assert.Equal(H("Ctrl+F1"), c.Value);
    }

    [Fact]
    public void Set_replaces_the_value_and_ends_capture()
    {
        var c = new HotkeyCapture(null);
        c.Begin();

        c.Set(H("Ctrl+Alt+F9"));

        Assert.False(c.Capturing);
        Assert.Equal("Ctrl+Alt+F9", c.Text);
        c.Set(null);
        Assert.Equal("", c.Text);
    }

    [Theory]
    [InlineData(VkShift, true)]
    [InlineData(VkControl, true)]
    [InlineData(VkMenu, true)]
    [InlineData(VkLWin, true)]
    [InlineData(VkRWin, true)]
    [InlineData(VkLShift, true)]
    [InlineData(VkRMenu, true)]
    [InlineData(VkF17, false)]
    public void Modifier_keys(uint vk, bool modifier)
    {
        Assert.Equal(modifier, HotkeyCapture.IsModifierKey(vk));
    }
}
