using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputCast;

public enum ActivationMode
{
    /// <summary>Active only while a ShareX screen recording is running.</summary>
    Auto,
    /// <summary>Always active.</summary>
    Always,
    /// <summary>Never active.</summary>
    Off,
}

public enum BubblePosition
{
    BottomCenter,
    BottomLeft,
    BottomRight,
    TopCenter,
}

public sealed class Settings
{
    public ActivationMode Mode { get; set; } = ActivationMode.Auto;

    // What to show
    public bool ShowClicks { get; set; } = true;
    public bool ShowKeys { get; set; } = true;
    /// <summary>Show plain typed text (letters, digits, ...). When false only shortcuts and special keys are shown.</summary>
    public bool ShowTyping { get; set; } = true;
    /// <summary>Show a modifier pressed and released on its own (e.g. just "Win").</summary>
    public bool ShowLoneModifiers { get; set; } = true;
    public bool ShowScroll { get; set; } = true;
    /// <summary>Also list mouse clicks in the key bubble (e.g. "Right Click"). Clicks combined with modifiers are always listed.</summary>
    public bool ShowClicksInBubble { get; set; } = false;
    /// <summary>Draw a small L / R / M letter next to the click indicator.</summary>
    public bool ShowClickLabel { get; set; } = true;

    // Click indicator look
    public int ClickRadius { get; set; } = 26;
    public int RippleDurationMs { get; set; } = 450;
    public string LeftColor { get; set; } = "#FFC107";
    public string RightColor { get; set; } = "#FF5252";
    public string MiddleColor { get; set; } = "#40C4FF";
    public string XButtonColor { get; set; } = "#B388FF";

    // Key bubble look
    public BubblePosition BubblePosition { get; set; } = BubblePosition.BottomCenter;
    public float FontSize { get; set; } = 18f;
    public int BubbleMargin { get; set; } = 40;
    public int MaxLines { get; set; } = 3;
    public int LingerMs { get; set; } = 2000;
    public int FadeMs { get; set; } = 350;
    public string BubbleBackground { get; set; } = "#E0181818";
    public string BubbleForeground { get; set; } = "#FFFFFFFF";
    public string KeyCapBackground { get; set; } = "#FF3A3A3A";

    /// <summary>Place the key bubble inside the ShareX recording region when one is detected.</summary>
    public bool FollowRecordingRegion { get; set; } = true;

    /// <summary>Global hotkey that pauses/resumes the overlay (e.g. while typing a password).</summary>
    public string PauseHotkey { get; set; } = "Ctrl+Alt+Shift+K";

    /// <summary>Window title prefixes that identify ShareX's recording toolbar (localized titles all start with "ShareX - ").</summary>
    public List<string> RecorderTitlePrefixes { get; set; } = ["ShareX - "];

    /// <summary>Process names whose recording window enables Auto mode.</summary>
    public List<string> RecorderProcessNames { get; set; } = ["ShareX"];

    // ---------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputCast");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                {
                    s.Save(); // writes back any newly added keys
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read {FilePath}:\n{ex.Message}\n\nDefaults will be used.", "InputCast",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new Settings();
        }

        var def = new Settings();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Non-fatal: settings just won't persist.
        }
    }

    public static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            string h = hex.TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            if (h.Length != 8) return fallback;
            return Color.FromArgb(Convert.ToInt32(h, 16));
        }
        catch
        {
            return fallback;
        }
    }
}
