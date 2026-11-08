using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ProfileHop.Core.Tray;

/// <summary>
/// Converts between the Windows hotkey control's value (HKM_GETHOTKEY / HKM_SETHOTKEY: low byte VK, high byte
/// HOTKEYF_SHIFT 1, HOTKEYF_CONTROL 2, HOTKEYF_ALT 4, HOTKEYF_EXT 8) and <see cref="Hotkey"/> (RegisterHotKey's
/// MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8). Note Shift and Alt swap bits. The control has no Win flag.
/// </summary>
public static class HotkeyControlCodec
{
    private const int HotkeyfShift = 0x1;
    private const int HotkeyfControl = 0x2;
    private const int HotkeyfAlt = 0x4;
    private const int HotkeyfExt = 0x8;

    /// <summary>Keys the control names with the extended-key bit (nav cluster, arrows, numpad divide).</summary>
    private static readonly HashSet<uint> ExtendedKeys = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x6F];

    /// <summary>Decodes a control value. 0 → <paramref name="hotkey"/> null (cleared). Unsupported keys fail.</summary>
    public static bool TryDecode(ushort value, out Hotkey? hotkey, [NotNullWhen(false)] out string? error)
    {
        hotkey = null;
        error = null;
        if (value == 0) return true;

        uint vk = (uint)(value & 0xFF);
        int flags = value >> 8;
        var mods = HotkeyModifiers.None;
        if ((flags & HotkeyfShift) != 0) mods |= HotkeyModifiers.Shift;
        if ((flags & HotkeyfControl) != 0) mods |= HotkeyModifiers.Control;
        if ((flags & HotkeyfAlt) != 0) mods |= HotkeyModifiers.Alt;

        if (vk == 0)
        {
            error = "Press a key together with the modifiers.";
            return false;
        }
        if (!HotkeyParser.IsSupportedKey(vk))
        {
            error = $"That key (0x{vk.ToString("X2", CultureInfo.InvariantCulture)}) is not supported. Use F1–F24, A–Z, 0–9, "
                + "Home, End, Insert, Delete, PageUp, PageDown, arrows, Space, Pause or the numpad.";
            return false;
        }

        hotkey = new Hotkey(mods, vk);
        return true;
    }

    /// <summary>Encodes for HKM_SETHOTKEY. A Win modifier is dropped (see <see cref="CanEncode"/>).</summary>
    public static ushort Encode(Hotkey hotkey)
    {
        var flags = 0;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) flags |= HotkeyfShift;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) flags |= HotkeyfControl;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) flags |= HotkeyfAlt;
        if (ExtendedKeys.Contains(hotkey.VirtualKey)) flags |= HotkeyfExt;
        return (ushort)((flags << 8) | (int)(hotkey.VirtualKey & 0xFF));
    }

    /// <summary>False when the control can't show the hotkey exactly (it uses Win).</summary>
    public static bool CanEncode(Hotkey hotkey) => !hotkey.Modifiers.HasFlag(HotkeyModifiers.Win);
}
