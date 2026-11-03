using System.Text;

namespace InputCast;

[Flags]
public enum Mods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8, AltGr = 16 }

internal static class KeyFormatter
{
    private static readonly Dictionary<Keys, string> Names = new()
    {
        [Keys.Return] = "Enter", [Keys.Back] = "Backspace", [Keys.Escape] = "Esc", [Keys.Space] = "Space",
        [Keys.Tab] = "Tab", [Keys.Delete] = "Del", [Keys.Insert] = "Ins", [Keys.Home] = "Home", [Keys.End] = "End",
        [Keys.PageUp] = "PgUp", [Keys.PageDown] = "PgDn",
        [Keys.Up] = "↑", [Keys.Down] = "↓", [Keys.Left] = "←", [Keys.Right] = "→",
        [Keys.CapsLock] = "Caps Lock", [Keys.NumLock] = "Num Lock", [Keys.Scroll] = "Scroll Lock",
        [Keys.PrintScreen] = "PrtSc", [Keys.Pause] = "Pause", [Keys.Apps] = "Menu",
        [Keys.Multiply] = "Num *", [Keys.Add] = "Num +", [Keys.Subtract] = "Num -",
        [Keys.Decimal] = "Num .", [Keys.Divide] = "Num /",
        [Keys.VolumeUp] = "Vol +", [Keys.VolumeDown] = "Vol -", [Keys.VolumeMute] = "Mute",
        [Keys.MediaPlayPause] = "Play/Pause", [Keys.MediaNextTrack] = "Next Track",
        [Keys.MediaPreviousTrack] = "Prev Track", [Keys.MediaStop] = "Stop",
        [Keys.BrowserBack] = "Browser Back", [Keys.BrowserForward] = "Browser Fwd",
        [Keys.BrowserRefresh] = "Refresh", [Keys.BrowserHome] = "Browser Home", [Keys.BrowserSearch] = "Search",
    };

    public static bool IsModifier(Keys k) => ModifierOf(k) != Mods.None;

    public static Mods ModifierOf(Keys k) => k switch
    {
        Keys.LControlKey or Keys.RControlKey or Keys.ControlKey => Mods.Ctrl,
        Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey => Mods.Shift,
        Keys.LMenu or Keys.Menu => Mods.Alt,
        Keys.RMenu => Mods.AltGr, // resolved to Alt or AltGr by the tracker depending on layout
        Keys.LWin or Keys.RWin => Mods.Win,
        _ => Mods.None,
    };

    public static string ModifierName(Mods m) => m switch
    {
        Mods.Ctrl => "Ctrl",
        Mods.Alt => "Alt",
        Mods.Shift => "Shift",
        Mods.Win => "Win",
        Mods.AltGr => "AltGr",
        _ => m.ToString(),
    };

    /// <summary>Returns the modifiers as chips in the conventional order Ctrl, Win, Alt, AltGr, Shift.</summary>
    public static IEnumerable<string> ModifierChips(Mods mods)
    {
        foreach (Mods m in new[] { Mods.Ctrl, Mods.Win, Mods.Alt, Mods.AltGr, Mods.Shift })
            if (mods.HasFlag(m)) yield return ModifierName(m);
    }

    /// <summary>True for keys that are shown as a key cap rather than as typed text.</summary>
    public static bool IsSpecial(Keys k) =>
        Names.ContainsKey(k) || k is >= Keys.F1 and <= Keys.F24;

    /// <summary>Human-readable key cap label, layout-aware for punctuation keys.</summary>
    public static string KeyName(Keys k, uint scanCode, IntPtr hkl)
    {
        if (Names.TryGetValue(k, out string? n)) return n;
        if (k is >= Keys.A and <= Keys.Z) return ((char)('A' + (k - Keys.A))).ToString();
        if (k is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k is >= Keys.NumPad0 and <= Keys.NumPad9) return "Num " + (k - Keys.NumPad0);
        if (k is >= Keys.F1 and <= Keys.F24) return "F" + (k - Keys.F1 + 1);

        // Punctuation / OEM keys: ask the active keyboard layout what the unshifted key produces.
        string? ch = Translate(k, scanCode, Mods.None, capsLock: false, hkl);
        if (!string.IsNullOrWhiteSpace(ch)) return ch.ToUpperInvariant();

        return k.ToString();
    }

    /// <summary>
    /// The text the key would type with the given modifiers on the given layout, or null if it is
    /// not printable (or a dead key). Uses the "do not change keyboard state" flag so dead keys
    /// typed in the foreground application are not disturbed.
    /// </summary>
    public static string? Translate(Keys k, uint scanCode, Mods mods, bool capsLock, IntPtr hkl)
    {
        var state = new byte[256];
        if (mods.HasFlag(Mods.Shift)) state[(int)Keys.ShiftKey] = state[(int)Keys.LShiftKey] = 0x80;
        if (mods.HasFlag(Mods.AltGr))
        {
            state[(int)Keys.ControlKey] = state[(int)Keys.LControlKey] = 0x80;
            state[(int)Keys.Menu] = state[(int)Keys.RMenu] = 0x80;
        }
        if (capsLock) state[(int)Keys.CapsLock] = 0x01;

        var sb = new StringBuilder(8);
        const uint DontChangeKeyboardState = 0x4;
        int rc = Native.ToUnicodeEx((uint)k, scanCode, state, sb, sb.Capacity, DontChangeKeyboardState, hkl);
        if (rc <= 0) return null;

        string s = sb.ToString(0, Math.Min(rc, sb.Length));
        foreach (char c in s)
            if (char.IsControl(c)) return null;
        return s;
    }

    public static IntPtr ForegroundLayout()
    {
        uint thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        return Native.GetKeyboardLayout(thread);
    }

    public static string ButtonName(MouseButton b) => b switch
    {
        MouseButton.Left => "Left Click",
        MouseButton.Right => "Right Click",
        MouseButton.Middle => "Middle Click",
        MouseButton.XButton1 => "Mouse Back",
        MouseButton.XButton2 => "Mouse Forward",
        _ => b.ToString(),
    };

    public static string ButtonLetter(MouseButton b) => b switch
    {
        MouseButton.Left => "L",
        MouseButton.Right => "R",
        MouseButton.Middle => "M",
        MouseButton.XButton1 => "X1",
        MouseButton.XButton2 => "X2",
        _ => "",
    };
}
