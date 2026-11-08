namespace ProfileHop.Core.Tray;

public enum CaptureOutcome
{
    /// <summary>Not for the field (Tab / Enter without modifiers): let the dialog navigate or press Save.</summary>
    NotHandled,
    /// <summary>Only modifiers so far; waiting for a key.</summary>
    Pending,
    Committed,
    Cleared,
    Cancelled,
    /// <summary>The key can't be a hotkey (no name, or a bare non-F-key); still capturing.</summary>
    Rejected,
}

/// <summary>
/// Key-capture logic for one Hotkeys-dialog field, fed with the focused field's WM_KEYDOWN / WM_SYSKEYDOWN (virtual
/// key plus the modifiers held at that moment). Modifier-only presses only preview; the first non-modifier key the
/// <see cref="HotkeyParser"/> accepts commits (bare F1–F24 allowed, other keys need a modifier). Esc cancels,
/// Backspace / Delete without modifiers clear. No hooks: it sees only keys sent to the focused field.
/// </summary>
public sealed class HotkeyCapture(Hotkey? initial)
{
    public const string Prompt = "Press a key…";

    private const uint VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkEscape = 0x1B, VkDelete = 0x2E;

    private Hotkey? _before;
    private string? _preview;

    public Hotkey? Value { get; private set; } = initial;

    public bool Capturing { get; private set; }

    /// <summary>What the field shows: the hotkey (empty = none), or while capturing the prompt, a modifier preview
    /// ("Ctrl+Alt+…") or why the last key was rejected.</summary>
    public string Text => Capturing ? _preview ?? Prompt : Value?.ToString() ?? "";

    /// <summary>Shift, Ctrl, Alt (either side) and both Win keys.</summary>
    public static bool IsModifierKey(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>For WM_GETDLGCODE: false for Tab and Enter without modifiers (dialog navigation / Save).</summary>
    public static bool WantsKey(uint vk, HotkeyModifiers held) => held != HotkeyModifiers.None || vk is not (VkTab or VkReturn);

    /// <summary>Field got focus.</summary>
    public void Begin()
    {
        _before = Value;
        _preview = null;
        Capturing = true;
    }

    /// <summary>Field lost focus: an unfinished capture changes nothing.</summary>
    public void End()
    {
        Capturing = false;
        _preview = null;
    }

    /// <summary>Clear button / Reset to defaults.</summary>
    public void Set(Hotkey? value)
    {
        Value = value;
        End();
    }

    public CaptureOutcome KeyDown(uint vk, HotkeyModifiers held)
    {
        if (!WantsKey(vk, held)) return CaptureOutcome.NotHandled;
        if (!Capturing) Begin();

        if (IsModifierKey(vk))
        {
            _preview = Preview(held);
            return CaptureOutcome.Pending;
        }
        if (held == HotkeyModifiers.None)
        {
            switch (vk)
            {
                case VkEscape:
                    Value = _before;
                    End();
                    return CaptureOutcome.Cancelled;
                case VkBack or VkDelete:
                    Set(null);
                    return CaptureOutcome.Cleared;
            }
        }

        if (!HotkeyParser.IsSupportedKey(vk))
        {
            _preview = "That key can't be a hotkey";
            return CaptureOutcome.Rejected;
        }
        var text = new Hotkey(held, vk).ToString();
        if (!HotkeyParser.TryParse(text, out var hotkey, out _))
        {
            _preview = $"{text} needs a modifier";
            return CaptureOutcome.Rejected;
        }
        Set(hotkey);
        return CaptureOutcome.Committed;
    }

    /// <summary>A key was released: refresh the modifier preview.</summary>
    public void KeyUp(HotkeyModifiers held)
    {
        if (Capturing) _preview = held == HotkeyModifiers.None ? null : Preview(held);
    }

    private static string Preview(HotkeyModifiers held)
    {
        if (held == HotkeyModifiers.None) return Prompt;
        var parts = new List<string>(4);
        if (held.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (held.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (held.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (held.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join('+', parts) + "+…";
    }
}
