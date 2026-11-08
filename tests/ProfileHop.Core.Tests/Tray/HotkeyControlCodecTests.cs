using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class HotkeyControlCodecTests
{
    // HKM_GETHOTKEY: low byte = VK, high byte = HOTKEYF_SHIFT 1, HOTKEYF_CONTROL 2, HOTKEYF_ALT 4, HOTKEYF_EXT 8.
    private static ushort Hk(uint vk, int flags) => (ushort)((flags << 8) | (int)vk);

    [Fact]
    public void Zero_means_disabled()
    {
        Assert.True(HotkeyControlCodec.TryDecode(0, out var hotkey, out _));
        Assert.Null(hotkey);
    }

    [Fact]
    public void Ctrl_alt_f9_from_control()
    {
        Assert.True(HotkeyControlCodec.TryDecode(Hk(0x78, 2 | 4), out var hotkey, out _));
        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x78), hotkey);
    }

    [Fact]
    public void Shift_and_alt_bits_are_swapped_between_the_two_apis()
    {
        Assert.True(HotkeyControlCodec.TryDecode(Hk('Q', 1), out var shift, out _));
        Assert.Equal(HotkeyModifiers.Shift, shift!.Value.Modifiers);
        Assert.True(HotkeyControlCodec.TryDecode(Hk('Q', 4), out var alt, out _));
        Assert.Equal(HotkeyModifiers.Alt, alt!.Value.Modifiers);

        Assert.Equal(Hk('Q', 1), HotkeyControlCodec.Encode(new Hotkey(HotkeyModifiers.Shift, 'Q')));
        Assert.Equal(Hk('Q', 4), HotkeyControlCodec.Encode(new Hotkey(HotkeyModifiers.Alt, 'Q')));
    }

    [Fact]
    public void Ext_flag_is_ignored_when_decoding()
    {
        Assert.True(HotkeyControlCodec.TryDecode(Hk(0x24, 2 | 8), out var hotkey, out _)); // Ctrl+Home
        Assert.Equal(new Hotkey(HotkeyModifiers.Control, 0x24), hotkey);
    }

    [Theory]
    [InlineData(0x24u)] // Home
    [InlineData(0x2Eu)] // Delete
    [InlineData(0x25u)] // Left
    [InlineData(0x6Fu)] // Numpad divide
    public void Extended_keys_get_ext_flag_when_encoding(uint vk)
    {
        Assert.Equal(Hk(vk, 2 | 8), HotkeyControlCodec.Encode(new Hotkey(HotkeyModifiers.Control, vk)));
    }

    [Fact]
    public void Non_extended_key_has_no_ext_flag()
    {
        Assert.Equal(Hk(0x78, 2 | 4 | 1), HotkeyControlCodec.Encode(HotkeyParser.Parse("Ctrl+Alt+Shift+F9")));
    }

    [Fact]
    public void Win_modifier_cannot_be_shown_by_the_control()
    {
        var hk = HotkeyParser.Parse("Win+Ctrl+F9");
        Assert.False(HotkeyControlCodec.CanEncode(hk));
        Assert.True(HotkeyControlCodec.CanEncode(HotkeyParser.Parse("Ctrl+F9")));
        Assert.Equal(Hk(0x78, 2), HotkeyControlCodec.Encode(hk)); // Win dropped
    }

    [Fact]
    public void Unsupported_key_is_rejected_with_a_message()
    {
        Assert.False(HotkeyControlCodec.TryDecode(Hk(0xBA, 2), out _, out var error)); // VK_OEM_1, layout dependent
        Assert.Contains("not supported", error);
    }

    [Fact]
    public void Round_trips_every_parser_key_through_control_and_text()
    {
        foreach (var name in HotkeyParser.KeyNames)
        {
            var hk = HotkeyParser.Parse("Ctrl+Alt+" + name);
            Assert.True(HotkeyControlCodec.TryDecode(HotkeyControlCodec.Encode(hk), out var back, out var error), error);
            Assert.Equal(hk, back);
            Assert.Equal(hk, HotkeyParser.Parse(back!.Value.ToString()));
        }
    }
}
