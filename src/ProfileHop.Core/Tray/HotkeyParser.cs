using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ProfileHop.Core.Tray;

/// <summary>RegisterHotKey modifier flags (MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN). MOD_NOREPEAT is added by the tray.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A parsed hotkey: RegisterHotKey modifiers plus a virtual-key code. <see cref="ToString"/> gives the
/// canonical config text ("Ctrl+Alt+F9") that <see cref="HotkeyParser.Parse"/> reads back.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public override string ToString() => HotkeyParser.Format(this);
}

/// <summary>
/// Parses and formats strings like "Ctrl+Alt+F9" (case-insensitive). Modifiers: Ctrl/Control, Alt, Shift, Win.
/// Keys: F1–F24, A–Z, 0–9, and the names in <see cref="KeyNames"/> (Home, PageUp, Numpad5, …).
/// </summary>
public static class HotkeyParser
{
    private const uint VkF1 = 0x70;

    private static readonly (string Name, uint Vk)[] Named =
    [
        ("Space", 0x20), ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23), ("Home", 0x24),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
        ("Insert", 0x2D), ("Delete", 0x2E), ("Pause", 0x13),
        ("Numpad0", 0x60), ("Numpad1", 0x61), ("Numpad2", 0x62), ("Numpad3", 0x63), ("Numpad4", 0x64),
        ("Numpad5", 0x65), ("Numpad6", 0x66), ("Numpad7", 0x67), ("Numpad8", 0x68), ("Numpad9", 0x69),
        ("NumpadMultiply", 0x6A), ("NumpadAdd", 0x6B), ("NumpadSubtract", 0x6D), ("NumpadDecimal", 0x6E),
        ("NumpadDivide", 0x6F),
    ];

    private static readonly Dictionary<string, uint> NameToVk = BuildNameToVk();
    private static readonly Dictionary<uint, string> VkToName = BuildVkToName();

    /// <summary>Every canonical key name the parser accepts (excluding modifiers).</summary>
    public static IReadOnlyList<string> KeyNames { get; } = [.. VkToName.Values];

    /// <summary>True if <paramref name="vk"/> has a name, i.e. it can be written to config and parsed back.</summary>
    public static bool IsSupportedKey(uint vk) => VkToName.ContainsKey(vk);

    public static Hotkey Parse(string text) =>
        TryParse(text, out var hotkey, out var error) ? hotkey : throw new FormatException(error);

    public static string Format(Hotkey hotkey)
    {
        var parts = new List<string>(5);
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VkToName.TryGetValue(hotkey.VirtualKey, out var name)
            ? name
            : "0x" + hotkey.VirtualKey.ToString("X2", CultureInfo.InvariantCulture));
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey, [NotNullWhen(false)] out string? error)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty.";
            return false;
        }

        var mods = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                error = $"Hotkey '{text}' has an empty part.";
                return false;
            }

            var mod = ParseModifier(part);
            if (mod != HotkeyModifiers.None)
            {
                if ((mods & mod) != 0)
                {
                    error = $"Hotkey '{text}' has a duplicate modifier '{part}'.";
                    return false;
                }
                mods |= mod;
                continue;
            }

            if (!NameToVk.TryGetValue(part, out var vk))
            {
                error = $"Hotkey '{text}' has an unknown key '{part}'. Use Ctrl, Alt, Shift, Win plus F1–F24, A–Z, 0–9, "
                    + "Home, End, Insert, Delete, PageUp, PageDown, Up, Down, Left, Right, Space, Pause or Numpad0–9.";
                return false;
            }
            if (key is not null)
            {
                error = $"Hotkey '{text}' has more than one key.";
                return false;
            }
            key = vk;
        }

        if (key is null)
        {
            error = $"Hotkey '{text}' has no key, only modifiers.";
            return false;
        }

        var isFunctionKey = key >= VkF1 && key < VkF1 + 24;
        if (mods == HotkeyModifiers.None && !isFunctionKey)
        {
            error = $"Hotkey '{text}' needs a modifier (Ctrl, Alt, Shift or Win); only F-keys may stand alone.";
            return false;
        }

        hotkey = new Hotkey(mods, key.Value);
        error = null;
        return true;
    }

    private static HotkeyModifiers ParseModifier(string part) => part.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Control,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" or "WINDOWS" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static Dictionary<uint, string> BuildVkToName()
    {
        var map = new Dictionary<uint, string>();
        for (uint i = 0; i < 24; i++) map[VkF1 + i] = "F" + (i + 1).ToString(CultureInfo.InvariantCulture);
        for (var c = 'A'; c <= 'Z'; c++) map[c] = c.ToString();
        for (var c = '0'; c <= '9'; c++) map[c] = c.ToString();
        foreach (var (name, vk) in Named) map[vk] = name;
        return map;
    }

    private static Dictionary<string, uint> BuildNameToVk()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vk, name) in BuildVkToName()) map[name] = vk;
        map["Del"] = 0x2E;
        map["Ins"] = 0x2D;
        map["PgUp"] = 0x21;
        map["PgDn"] = 0x22;
        return map;
    }
}
