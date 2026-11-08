using System.Diagnostics.CodeAnalysis;

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

/// <summary>A parsed hotkey: RegisterHotKey modifiers plus a virtual-key code.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(HotkeyParser.KeyName(VirtualKey));
        return string.Join('+', parts);
    }
}

/// <summary>Parses strings like "Ctrl+Alt+F9" (case-insensitive; Ctrl/Control, Alt, Shift, Win; F1–F24, A–Z, 0–9).</summary>
public static class HotkeyParser
{
    private const uint VkF1 = 0x70;

    public static Hotkey Parse(string text) =>
        TryParse(text, out var hotkey, out var error) ? hotkey : throw new FormatException(error);

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

            var vk = ParseKey(part);
            if (vk is null)
            {
                error = $"Hotkey '{text}' has an unknown key '{part}'. Use Ctrl, Alt, Shift, Win plus F1–F24, A–Z or 0–9.";
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

    internal static string KeyName(uint vk) =>
        vk >= VkF1 && vk < VkF1 + 24 ? $"F{vk - VkF1 + 1}" : ((char)vk).ToString();

    private static HotkeyModifiers ParseModifier(string part) => part.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Control,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" or "WINDOWS" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static uint? ParseKey(string part)
    {
        var upper = part.ToUpperInvariant();
        if (upper.Length == 1 && (upper[0] is >= 'A' and <= 'Z' || upper[0] is >= '0' and <= '9'))
            return upper[0];
        if (upper.Length is 2 or 3 && upper[0] == 'F' && upper[1] != '0'
            && int.TryParse(upper.AsSpan(1), System.Globalization.NumberStyles.None, null, out var n)
            && n is >= 1 and <= 24)
            return VkF1 + (uint)(n - 1);
        return null;
    }
}
