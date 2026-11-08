using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_ctrl_alt_f9()
    {
        var hk = HotkeyParser.Parse("Ctrl+Alt+F9");
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hk.Modifiers);
        Assert.Equal(0x78u, hk.VirtualKey); // VK_F9
    }

    [Theory]
    [InlineData("ctrl+alt+f12", 0x7Bu)]
    [InlineData("CTRL + ALT + F1", 0x70u)]
    [InlineData("Control+Alt+F24", 0x87u)]
    public void Is_case_and_space_insensitive(string text, uint vk)
    {
        var hk = HotkeyParser.Parse(text);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hk.Modifiers);
        Assert.Equal(vk, hk.VirtualKey);
    }

    [Fact]
    public void Parses_shift_win_letter()
    {
        var hk = HotkeyParser.Parse("Shift+Win+p");
        Assert.Equal(HotkeyModifiers.Shift | HotkeyModifiers.Win, hk.Modifiers);
        Assert.Equal((uint)'P', hk.VirtualKey);
    }

    [Fact]
    public void Parses_digit()
    {
        var hk = HotkeyParser.Parse("Ctrl+Shift+7");
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, hk.Modifiers);
        Assert.Equal((uint)'7', hk.VirtualKey);
    }

    [Fact]
    public void Modifier_flags_match_win32_values()
    {
        Assert.Equal(1u, (uint)HotkeyModifiers.Alt);
        Assert.Equal(2u, (uint)HotkeyModifiers.Control);
        Assert.Equal(4u, (uint)HotkeyModifiers.Shift);
        Assert.Equal(8u, (uint)HotkeyModifiers.Win);
    }

    [Fact]
    public void Bare_function_key_is_allowed()
    {
        var hk = HotkeyParser.Parse("F8");
        Assert.Equal(HotkeyModifiers.None, hk.Modifiers);
        Assert.Equal(0x77u, hk.VirtualKey);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("Ctrl+Alt", "no key")]
    [InlineData("Ctrl+Alt+F25", "unknown key 'F25'")]
    [InlineData("Ctrl+Alt+F0", "unknown key 'F0'")]
    [InlineData("Ctrl+Hyper+F9", "unknown key 'Hyper'")]
    [InlineData("Ctrl+F9+F10", "more than one key")]
    [InlineData("Ctrl++F9", "empty part")]
    [InlineData("Ctrl+Ctrl+F9", "duplicate modifier 'Ctrl'")]
    [InlineData("A", "needs a modifier")]
    [InlineData("5", "needs a modifier")]
    public void Rejects_garbage_with_clear_message(string text, string fragment)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out var error));
        Assert.Contains(fragment, error);
        var ex = Assert.Throws<FormatException>(() => HotkeyParser.Parse(text));
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void ToString_round_trips_canonical_form()
    {
        Assert.Equal("Ctrl+Alt+F9", HotkeyParser.Parse("alt+ctrl+f9").ToString());
        Assert.Equal("Ctrl+Alt+Shift+Win+Q", HotkeyParser.Parse("win+shift+alt+ctrl+q").ToString());
    }
}
