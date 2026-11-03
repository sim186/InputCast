using System.Diagnostics;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace InputCast;

internal sealed class TrayApp : ApplicationContext
{
    public const string ProjectUrl = "https://github.com/sim186/InputCast";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int PauseHotkeyId = 1;

    private Settings _settings;
    private readonly InputHooks _hooks;
    private readonly ClickOverlay _clicks;
    private readonly KeyBubble _bubble;
    private readonly RecordingDetector _detector = new();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 400 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _paused;
    private bool _active;
    private bool _demoRunning;
    private Icon? _currentIcon;
    private string _lastState = "";

    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _pauseItem = null!;

    private long Now => _clock.ElapsedMilliseconds;

    public TrayApp(bool runDemo)
    {
        _settings = Settings.Load();
        _hooks = new InputHooks();
        _hooks.Input += OnInput;
        _clicks = new ClickOverlay(() => _settings, () => _hooks.CursorPosition);
        _bubble = new KeyBubble(() => _settings, () => _detector.IsRecording ? _detector.Region : null,
            () => _hooks.Installed ? _hooks.CursorPosition : Native.CursorPos());

        _tray = new NotifyIcon { Text = "InputCast", Visible = true };
        _tray.ContextMenuStrip = BuildMenu();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) TogglePause();
        };

        _hotkeyWindow = new HotkeyWindow(id =>
        {
            if (id == PauseHotkeyId) TogglePause();
        });
        RegisterPauseHotkey();

        _frameTimer.Tick += (_, _) => Frame();
        _pollTimer.Tick += (_, _) => Evaluate();
        _pollTimer.Start();
        Evaluate();

        if (runDemo) RunDemo();
        else if (!File.Exists(Path.Combine(Settings.Directory, ".welcomed")))
        {
            try { File.WriteAllText(Path.Combine(Settings.Directory, ".welcomed"), ""); } catch { }
            _tray.ShowBalloonTip(5000, "InputCast is running",
                "Clicks and keys are shown automatically while ShareX records. Right-click the tray icon for options.",
                ToolTipIcon.Info);
        }
    }

    // ------------------------------------------------------------------ state

    private void Evaluate()
    {
        bool wantRecording = _settings.Mode == ActivationMode.Auto;
        if (wantRecording || _settings.FollowRecordingRegion)
            _detector.Poll(_settings, Now);

        bool active = !_paused && _settings.Mode switch
        {
            ActivationMode.Always => true,
            ActivationMode.Auto => _detector.IsRecording,
            _ => false,
        };

        if (active != _active)
        {
            _active = active;
            if (active)
            {
                _bubble.ResetModifiers();
                _hooks.Install();
            }
            else
            {
                _hooks.Uninstall();
                if (!_demoRunning)
                {
                    _clicks.Clear();
                    _bubble.Clear();
                }
            }
        }

        UpdateFrameTimer();
        UpdateTrayState();
    }

    private void UpdateFrameTimer()
    {
        bool needed = _active || _demoRunning || _clicks.Busy || _bubble.Busy;
        if (needed && !_frameTimer.Enabled) _frameTimer.Start();
        else if (!needed && _frameTimer.Enabled) _frameTimer.Stop();
    }

    private void OnInput(InputEvent e)
    {
        if (!_active && !_demoRunning) return;
        long now = Now;
        _clicks.OnInput(e, now);
        _bubble.OnInput(e, now);
    }

    private void Frame()
    {
        long now = Now;
        _clicks.Tick(now);
        _bubble.Tick(now);
        if (!_active && !_demoRunning && !_clicks.Busy && !_bubble.Busy) _frameTimer.Stop();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Checked = _paused;
        Evaluate();
        _tray.ShowBalloonTip(1500, "InputCast", _paused ? "Paused - nothing is shown." : "Resumed.", ToolTipIcon.None);
    }

    private void UpdateTrayState()
    {
        string state = _paused ? "Paused"
            : _settings.Mode == ActivationMode.Off ? "Off"
            : _active ? (_settings.Mode == ActivationMode.Auto ? "Active - ShareX is recording" : "Active (always on)")
            : "Waiting for a ShareX recording";

        if (state == _lastState) return;
        _lastState = state;

        _statusItem.Text = state;
        _tray.Text = "InputCast - " + (state.Length > 50 ? state[..50] : state);

        Color dot = _paused ? Color.FromArgb(255, 193, 7)
            : _active ? Color.FromArgb(229, 57, 53)
            : Color.FromArgb(140, 140, 140);
        var old = _currentIcon;
        _currentIcon = DrawIcon(dot);
        _tray.Icon = _currentIcon;
        if (old != null)
        {
            Native.DestroyIcon(old.Handle);
            old.Dispose();
        }
    }

    // ------------------------------------------------------------------ menu

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("…") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var mode = new ToolStripMenuItem("Activation");
        void AddMode(string text, ActivationMode m)
        {
            var item = new ToolStripMenuItem(text) { Tag = m };
            item.Click += (_, _) => { _settings.Mode = m; SaveAndApply(); };
            mode.DropDownItems.Add(item);
        }
        AddMode("Auto - only while ShareX records", ActivationMode.Auto);
        AddMode("Always on", ActivationMode.Always);
        AddMode("Off", ActivationMode.Off);
        mode.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripMenuItem i in mode.DropDownItems) i.Checked = (ActivationMode)i.Tag! == _settings.Mode;
        };
        menu.Items.Add(mode);

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Toggle("Show mouse clicks", () => _settings.ShowClicks, v => _settings.ShowClicks = v));
        menu.Items.Add(Toggle("Show keys", () => _settings.ShowKeys, v => _settings.ShowKeys = v));
        menu.Items.Add(Toggle("Show typed text (off = shortcuts only)", () => _settings.ShowTyping, v => _settings.ShowTyping = v));
        menu.Items.Add(Toggle("Show scroll wheel", () => _settings.ShowScroll, v => _settings.ShowScroll = v));
        menu.Items.Add(Toggle("List every click in the key bubble", () => _settings.ShowClicksInBubble, v => _settings.ShowClicksInBubble = v));
        menu.Items.Add(Toggle("Keep key bubble inside the recorded region", () => _settings.FollowRecordingRegion, v => _settings.FollowRecordingRegion = v));

        var pos = new ToolStripMenuItem("Key bubble position");
        foreach (BubblePosition p in Enum.GetValues<BubblePosition>())
        {
            var item = new ToolStripMenuItem(SplitWords(p.ToString())) { Tag = p };
            item.Click += (_, _) => { _settings.BubblePosition = p; SaveAndApply(); };
            pos.DropDownItems.Add(item);
        }
        pos.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripMenuItem i in pos.DropDownItems) i.Checked = (BubblePosition)i.Tag! == _settings.BubblePosition;
        };
        menu.Items.Add(pos);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Show demo", null, (_, _) => RunDemo()));
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        startup.Click += (_, _) => SetStartWithWindows(!IsStartWithWindows());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripMenuItem("Edit settings file…", null, (_, _) =>
            Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true })));
        menu.Items.Add(new ToolStripMenuItem("Reload settings", null, (_, _) => ReloadSettings()));
        menu.Items.Add(new ToolStripMenuItem("About / GitHub", null, (_, _) =>
            Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true })));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        menu.Opening += (_, _) =>
        {
            startup.Checked = IsStartWithWindows();
            _pauseItem.Text = $"Pause    ({_settings.PauseHotkey})";
            _pauseItem.Checked = _paused;
            foreach (var i in menu.Items.OfType<ToolStripMenuItem>())
                if (i.Tag is Func<bool> get) i.Checked = get();
        };

        return menu;
    }

    private ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Tag = get };
        item.Click += (_, _) => { set(!get()); SaveAndApply(); };
        return item;
    }

    private static string SplitWords(string s) =>
        string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLower(c) : c.ToString()));

    private void SaveAndApply()
    {
        _settings.Save();
        _lastState = "";
        Evaluate();
    }

    private void ReloadSettings()
    {
        _settings = Settings.Load();
        RegisterPauseHotkey();
        _clicks.Clear();
        _bubble.Clear();
        _lastState = "";
        Evaluate();
    }

    private void RegisterPauseHotkey()
    {
        Native.UnregisterHotKey(_hotkeyWindow.Handle, PauseHotkeyId);
        _bubble.IgnoreCombination(null);
        if (!Hotkey.TryParse(_settings.PauseHotkey, out uint mods, out Keys key, out var chips)) return;
        if (Native.RegisterHotKey(_hotkeyWindow.Handle, PauseHotkeyId, mods | Native.MOD_NOREPEAT, (uint)key))
            _bubble.IgnoreCombination(chips);
    }

    private static bool IsStartWithWindows()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("InputCast") is string;
    }

    private static void SetStartWithWindows(bool enable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) k.SetValue("InputCast", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("InputCast", false);
    }

    // ------------------------------------------------------------------ demo

    private void RunDemo()
    {
        if (_demoRunning) return;
        _demoRunning = true;
        UpdateFrameTimer();

        Rectangle wa = Native.WorkAreaAt(Native.CursorPos());
        float sc = Native.ScaleAt(new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2));
        Point c = new(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
        Point P(int dx, int dy) => new(c.X + (int)(dx * sc), c.Y + (int)(dy * sc));

        var steps = new List<(int delayMs, Action action)>();
        void Key(Keys k, bool down) => _hooks.Simulate(new InputEvent(down ? InputKind.KeyDown : InputKind.KeyUp, k));
        void Tap(Keys k) { Key(k, true); Key(k, false); }
        void Mouse(MouseButton b, Point p, bool down) =>
            _hooks.Simulate(new InputEvent(down ? InputKind.MouseDown : InputKind.MouseUp, Button: b, Location: p));

        steps.Add((0, () => Mouse(MouseButton.Left, P(-200, -60), true)));
        steps.Add((250, () => Mouse(MouseButton.Left, P(-200, -60), false)));
        steps.Add((350, () => Mouse(MouseButton.Right, P(0, -60), true)));
        steps.Add((300, () => Mouse(MouseButton.Right, P(0, -60), false)));
        steps.Add((350, () => Mouse(MouseButton.Middle, P(200, -60), true)));
        steps.Add((300, () => Mouse(MouseButton.Middle, P(200, -60), false)));
        foreach (Keys k in new[] { Keys.H, Keys.E, Keys.L, Keys.L, Keys.O, Keys.Space, Keys.S, Keys.H, Keys.A, Keys.R, Keys.E, Keys.X })
            steps.Add((90, () => Tap(k)));
        steps.Add((600, () => { Key(Keys.LControlKey, true); Key(Keys.LShiftKey, true); Tap(Keys.S); Key(Keys.LShiftKey, false); Key(Keys.LControlKey, false); }));
        steps.Add((600, () => { Key(Keys.LControlKey, true); Tap(Keys.C); }));
        steps.Add((250, () => { Tap(Keys.C); Key(Keys.LControlKey, false); }));
        steps.Add((500, () => _hooks.Simulate(new InputEvent(InputKind.Wheel, Location: c, WheelDelta: -120))));
        steps.Add((120, () => _hooks.Simulate(new InputEvent(InputKind.Wheel, Location: c, WheelDelta: -120))));
        steps.Add((120, () => _hooks.Simulate(new InputEvent(InputKind.Wheel, Location: c, WheelDelta: -120))));
        steps.Add((2500, () => { }));

        int index = 0;
        var t = new System.Windows.Forms.Timer { Interval = 1 };
        Point saved = _hooks.CursorPosition;
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (index >= steps.Count)
            {
                t.Dispose();
                _demoRunning = false;
                _hooks.SimulateMove(Native.CursorPos());
                _bubble.ResetModifiers();
                UpdateFrameTimer();
                return;
            }
            var (_, action) = steps[index++];
            action();
            if (index < steps.Count) t.Interval = Math.Max(1, steps[index].delayMs);
            t.Start();
        };
        t.Start();
    }

    // ------------------------------------------------------------------ icon

    private static Icon DrawIcon(Color dot)
    {
        int size = (int)(16 * Math.Max(1f, Native.ScaleAt(Native.CursorPos())));
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float u = size / 16f;
            using (var cap = ClickOverlay.RoundedRect(new RectangleF(0.5f * u, 2.5f * u, 15 * u, 11 * u), 2.5f * u))
            using (var fill = new SolidBrush(Color.FromArgb(240, 245, 245, 245)))
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 1.2f * u))
            {
                g.FillPath(fill, cap);
                g.DrawPath(pen, cap);
            }
            using (var key = new SolidBrush(Color.FromArgb(70, 70, 70)))
            {
                for (int i = 0; i < 4; i++) g.FillRectangle(key, (2.5f + i * 2.6f) * u, 5f * u, 1.6f * u, 1.6f * u);
                g.FillRectangle(key, 3.5f * u, 8.5f * u, 7f * u, 1.6f * u);
            }
            using var dotBrush = new SolidBrush(dot);
            g.FillEllipse(dotBrush, 9.5f * u, 8f * u, 6f * u, 6f * u);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ------------------------------------------------------------------ teardown

    protected override void ExitThreadCore()
    {
        _frameTimer.Stop();
        _pollTimer.Stop();
        _hooks.Dispose();
        _clicks.Dispose();
        _bubble.Dispose();
        Native.UnregisterHotKey(_hotkeyWindow.Handle, PauseHotkeyId);
        _hotkeyWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public HotkeyWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams { Caption = "InputCast hotkeys" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) _onHotkey((int)m.WParam);
            base.WndProc(ref m);
        }
    }
}

internal static class Hotkey
{
    /// <summary>Parses "Ctrl+Alt+Shift+K" style strings.</summary>
    public static bool TryParse(string text, out uint mods, out Keys key, out List<string> chips)
    {
        mods = 0;
        key = Keys.None;
        chips = [];
        Mods display = Mods.None;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Native.MOD_CONTROL; display |= Mods.Ctrl; break;
                case "alt": mods |= Native.MOD_ALT; display |= Mods.Alt; break;
                case "shift": mods |= Native.MOD_SHIFT; display |= Mods.Shift; break;
                case "win": mods |= Native.MOD_WIN; display |= Mods.Win; break;
                default:
                    if (!Enum.TryParse(raw, true, out key)) return false;
                    break;
            }
        }

        if (key == Keys.None) return false;
        chips = [.. KeyFormatter.ModifierChips(display), KeyFormatter.KeyName(key, 0, IntPtr.Zero)];
        return true;
    }
}
