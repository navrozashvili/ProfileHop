using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class HotkeyFormatTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Home", 0x24u)]
    [InlineData("Ctrl+Alt+End", 0x23u)]
    [InlineData("Ctrl+Alt+Insert", 0x2Du)]
    [InlineData("Ctrl+Alt+Delete", 0x2Eu)]
    [InlineData("Ctrl+Alt+PageUp", 0x21u)]
    [InlineData("Ctrl+Alt+PageDown", 0x22u)]
    [InlineData("Ctrl+Alt+Up", 0x26u)]
    [InlineData("Ctrl+Alt+Down", 0x28u)]
    [InlineData("Ctrl+Alt+Left", 0x25u)]
    [InlineData("Ctrl+Alt+Right", 0x27u)]
    [InlineData("Ctrl+Alt+Space", 0x20u)]
    [InlineData("Ctrl+Alt+Pause", 0x13u)]
    [InlineData("Ctrl+Alt+Numpad0", 0x60u)]
    [InlineData("Ctrl+Alt+Numpad9", 0x69u)]
    [InlineData("Ctrl+Alt+NumpadMultiply", 0x6Au)]
    [InlineData("Ctrl+Alt+NumpadAdd", 0x6Bu)]
    [InlineData("Ctrl+Alt+NumpadSubtract", 0x6Du)]
    [InlineData("Ctrl+Alt+NumpadDecimal", 0x6Eu)]
    [InlineData("Ctrl+Alt+NumpadDivide", 0x6Fu)]
    public void Named_keys_parse_and_format(string text, uint vk)
    {
        var hk = HotkeyParser.Parse(text.ToLowerInvariant());
        Assert.Equal(vk, hk.VirtualKey);
        Assert.Equal(text, hk.ToString());
        Assert.Equal(text, HotkeyParser.Format(hk));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Del", "Ctrl+Alt+Delete")]
    [InlineData("Ctrl+Alt+Ins", "Ctrl+Alt+Insert")]
    [InlineData("Ctrl+Alt+PgUp", "Ctrl+Alt+PageUp")]
    [InlineData("Ctrl+Alt+PgDn", "Ctrl+Alt+PageDown")]
    public void Aliases_format_canonically(string alias, string canonical)
    {
        Assert.Equal(canonical, HotkeyParser.Parse(alias).ToString());
    }

    [Fact]
    public void Formatting_unknown_vk_gives_hex_and_does_not_parse()
    {
        var text = new Hotkey(HotkeyModifiers.Control, 0xBA).ToString();
        Assert.Equal("Ctrl+0xBA", text);
        Assert.False(HotkeyParser.TryParse(text, out _, out _));
    }

    [Fact]
    public void Every_key_name_round_trips()
    {
        Assert.Contains("F24", HotkeyParser.KeyNames);
        Assert.Contains("Z", HotkeyParser.KeyNames);
        Assert.Contains("0", HotkeyParser.KeyNames);
        foreach (var name in HotkeyParser.KeyNames)
        {
            var hk = HotkeyParser.Parse("Shift+Win+" + name);
            Assert.Equal("Shift+Win+" + name, HotkeyParser.Format(hk));
        }
    }
}
