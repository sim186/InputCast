using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace InputCast;

/// <summary>Turns key / button / wheel events into short lines ("Ctrl + Shift + S", "hello", "Scroll ↓ ×4") and draws them.</summary>
internal sealed class KeyBubble : IDisposable
{
    private sealed class Line
    {
        public List<string> Chips = [];
        public string Text = "";
        public bool IsText;
        public int Repeat = 1;
        public string Signature = "";
        public long UpdatedMs;
    }

    private const int MaxTextChars = 40;

    private readonly Func<Settings> _settings;
    private readonly Func<Rectangle?> _recordingRegion;
    private readonly Func<Point> _cursor;
    private readonly LayeredWindow _window = new();
    private readonly List<Line> _lines = [];

    private Mods _held;
    private bool _fakeCtrlDown;      // the synthetic LCtrl Windows sends before AltGr
    private bool _rightAltIsAltGr;
    private Mods _loneMods;
    private bool _loneValid;
    private bool _dirty;
    private string? _ignoredSignature;

    public KeyBubble(Func<Settings> settings, Func<Rectangle?> recordingRegion, Func<Point> cursor)
    {
        _settings = settings;
        _recordingRegion = recordingRegion;
        _cursor = cursor;
    }

    public bool Busy => _lines.Count > 0;

    /// <summary>A key combination that should never be shown (the pause hotkey).</summary>
    public void IgnoreCombination(IEnumerable<string>? chips) =>
        _ignoredSignature = chips == null ? null : string.Join("+", chips);

    public void OnInput(InputEvent e, long nowMs)
    {
        var s = _settings();
        switch (e.Kind)
        {
            case InputKind.KeyDown: KeyDown(e, s, nowMs); break;
            case InputKind.KeyUp: KeyUp(e, s, nowMs); break;
            case InputKind.MouseDown:
                _loneValid = false;
                if (!s.ShowKeys) break;
                Mods m = CommandMods();
                if (s.ShowClicksInBubble || m != Mods.None)
                    AddChips([.. KeyFormatter.ModifierChips(m), KeyFormatter.ButtonName(e.Button)], nowMs);
                break;
            case InputKind.Wheel:
            case InputKind.HWheel:
                _loneValid = false;
                if (!s.ShowKeys || !s.ShowScroll || e.WheelDelta == 0) break;
                string dir = e.Kind == InputKind.Wheel
                    ? (e.WheelDelta > 0 ? "Scroll ↑" : "Scroll ↓")
                    : (e.WheelDelta > 0 ? "Scroll →" : "Scroll ←");
                AddChips([.. KeyFormatter.ModifierChips(CommandMods()), dir], nowMs);
                break;
        }
    }

    /// <summary>Held modifiers relevant for shortcuts (AltGr is a text modifier, not a shortcut modifier).</summary>
    private Mods CommandMods() => _held & ~Mods.AltGr;

    private void KeyDown(InputEvent e, Settings s, long nowMs)
    {
        Keys k = e.Key;

        if (k == Keys.LControlKey && (e.ScanCode & 0x200) != 0)
        {
            _fakeCtrlDown = true;
            _rightAltIsAltGr = true;
            return;
        }

        Mods mod = KeyFormatter.ModifierOf(k);
        if (mod != Mods.None)
        {
            if (mod == Mods.AltGr && !(_fakeCtrlDown || _rightAltIsAltGr)) mod = Mods.Alt;
            if (_held == Mods.None)
            {
                _loneMods = Mods.None;
                _loneValid = true;
            }
            _held |= mod;
            _loneMods |= mod;
            return;
        }

        _loneValid = false;
        if (!s.ShowKeys) return;

        Mods cmd = CommandMods();
        bool hasCommand = (cmd & (Mods.Ctrl | Mods.Alt | Mods.Win)) != 0;
        IntPtr hkl = KeyFormatter.ForegroundLayout();

        if (!hasCommand && k == Keys.Space && (_held & Mods.Shift) == 0)
        {
            if (s.ShowTyping) AddText(" ", nowMs);
            return;
        }

        if (!hasCommand && !KeyFormatter.IsSpecial(k))
        {
            bool caps = (Native.GetKeyState((int)Keys.CapsLock) & 1) != 0;
            string? text = KeyFormatter.Translate(k, e.ScanCode, _held & (Mods.Shift | Mods.AltGr), caps, hkl);
            if (text != null)
            {
                if (s.ShowTyping) AddText(text, nowMs);
                return;
            }
        }

        bool isFunctionKey = k is >= Keys.F1 and <= Keys.F24;
        if (!s.ShowTyping && !hasCommand && !isFunctionKey) return;

        AddChips([.. KeyFormatter.ModifierChips(cmd), KeyFormatter.KeyName(k, e.ScanCode, hkl)], nowMs);
    }

    private void KeyUp(InputEvent e, Settings s, long nowMs)
    {
        Keys k = e.Key;
        if (k == Keys.LControlKey && (e.ScanCode & 0x200) != 0)
        {
            _fakeCtrlDown = false;
            return;
        }

        Mods mod = KeyFormatter.ModifierOf(k);
        if (mod == Mods.None) return;
        if (mod == Mods.AltGr && !_held.HasFlag(Mods.AltGr)) mod = Mods.Alt;

        // Generic/left/right variants of the same modifier map to one flag; only clear it when no variant is down.
        _held &= ~mod;

        if (_held == Mods.None && _loneValid && s.ShowKeys && s.ShowLoneModifiers && _loneMods != Mods.None)
            AddChips([.. KeyFormatter.ModifierChips(_loneMods)], nowMs);

        if (_held == Mods.None) _loneValid = false;
    }

    private void AddChips(List<string> chips, long nowMs)
    {
        string sig = string.Join("+", chips);
        if (sig == _ignoredSignature) return;

        var last = _lines.LastOrDefault();
        if (last != null && !last.IsText && last.Signature == sig && IsFresh(last, nowMs))
        {
            last.Repeat++;
            last.UpdatedMs = nowMs;
        }
        else
        {
            AddLine(new Line { Chips = chips, Signature = sig, UpdatedMs = nowMs });
        }
        _dirty = true;
    }

    private void AddText(string text, long nowMs)
    {
        var last = _lines.LastOrDefault();
        if (last != null && last.IsText && IsFresh(last, nowMs))
        {
            last.Text += text;
            if (last.Text.Length > MaxTextChars) last.Text = "…" + last.Text[^(MaxTextChars - 1)..];
            last.UpdatedMs = nowMs;
        }
        else
        {
            AddLine(new Line { IsText = true, Text = text, UpdatedMs = nowMs });
        }
        _dirty = true;
    }

    private bool IsFresh(Line l, long nowMs) => nowMs - l.UpdatedMs < _settings().LingerMs;

    private void AddLine(Line line)
    {
        _lines.Add(line);
        int max = Math.Max(1, _settings().MaxLines);
        while (_lines.Count > max) _lines.RemoveAt(0);
    }

    public void ResetModifiers()
    {
        _held = Mods.None;
        _fakeCtrlDown = false;
        _loneValid = false;
    }

    public void Clear()
    {
        _lines.Clear();
        ResetModifiers();
        _window.Hide();
    }

    // ------------------------------------------------------------------ rendering

    public void Tick(long nowMs)
    {
        var s = _settings();
        bool fading = false;

        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            long age = nowMs - _lines[i].UpdatedMs;
            if (age >= s.LingerMs + s.FadeMs) { _lines.RemoveAt(i); _dirty = true; }
            else if (age >= s.LingerMs) fading = true;
        }

        if (_lines.Count == 0)
        {
            _window.Hide();
            _dirty = false;
            return;
        }

        if (_dirty || fading) Render(s, nowMs);
        _dirty = false;
    }

    private void Render(Settings s, long nowMs)
    {
        Rectangle? region = s.FollowRecordingRegion ? _recordingRegion() : null;
        Rectangle anchor = region ?? Native.WorkAreaAt(_cursor());
        float scale = Native.ScaleAt(new Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2));

        Color bg = Settings.ParseColor(s.BubbleBackground, Color.FromArgb(224, 24, 24, 24));
        Color fg = Settings.ParseColor(s.BubbleForeground, Color.White);
        Color cap = Settings.ParseColor(s.KeyCapBackground, Color.FromArgb(58, 58, 58));

        using var font = new Font("Segoe UI Semibold", s.FontSize * scale * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", s.FontSize * 0.8f * scale * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        var fmt = StringFormat.GenericTypographic;
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        float padX = 14 * scale, padY = 8 * scale, capPadX = 9 * scale, capPadY = 3 * scale;
        float gap = 6 * scale, lineGap = 6 * scale, radius = 10 * scale;

        // Measure
        var layouts = new List<(Line line, float width, float height)>();
        using (var probe = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(probe))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            float textH = g.MeasureString("Ag", font, PointF.Empty, fmt).Height;
            foreach (var line in _lines)
            {
                float w = 0;
                if (line.IsText)
                {
                    w = g.MeasureString(DisplayText(line), font, PointF.Empty, fmt).Width;
                }
                else
                {
                    for (int i = 0; i < line.Chips.Count; i++)
                    {
                        if (i > 0) w += gap + g.MeasureString("+", small, PointF.Empty, fmt).Width + gap;
                        w += g.MeasureString(line.Chips[i], font, PointF.Empty, fmt).Width + 2 * capPadX;
                    }
                }
                if (line.Repeat > 1) w += gap + g.MeasureString("×" + line.Repeat, small, PointF.Empty, fmt).Width;
                layouts.Add((line, w + 2 * padX, textH + 2 * padY + (line.IsText ? 0 : 2 * capPadY)));
            }
        }

        int width = (int)Math.Ceiling(layouts.Max(l => l.width)) + 2;
        int height = (int)Math.Ceiling(layouts.Sum(l => l.height) + lineGap * (layouts.Count - 1)) + 2;

        using var bmp = LayeredWindow.NewCanvas(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            float y = 1;
            foreach (var (line, w, h) in layouts)
            {
                long age = nowMs - line.UpdatedMs;
                float alpha = age < s.LingerMs ? 1f : Math.Clamp(1f - (age - s.LingerMs) / (float)Math.Max(1, s.FadeMs), 0f, 1f);

                float x = s.BubblePosition switch
                {
                    BubblePosition.BottomLeft => 1,
                    BubblePosition.BottomRight => width - 1 - w,
                    _ => (width - w) / 2f,
                };

                using (var path = ClickOverlay.RoundedRect(new RectangleF(x, y, w, h), radius))
                using (var b = new SolidBrush(Fade(bg, alpha)))
                    g.FillPath(b, path);

                using var fgBrush = new SolidBrush(Fade(fg, alpha));
                using var dimBrush = new SolidBrush(Fade(Color.FromArgb(170, fg), alpha));
                using var capBrush = new SolidBrush(Fade(cap, alpha));
                float cx = x + padX;

                if (line.IsText)
                {
                    string shown = DisplayText(line);
                    g.DrawString(shown, font, fgBrush, cx, y + padY, fmt);
                    cx += g.MeasureString(shown, font, PointF.Empty, fmt).Width;
                }
                else
                {
                    for (int i = 0; i < line.Chips.Count; i++)
                    {
                        if (i > 0)
                        {
                            cx += gap;
                            SizeF plus = g.MeasureString("+", small, PointF.Empty, fmt);
                            g.DrawString("+", small, dimBrush, cx, y + (h - plus.Height) / 2, fmt);
                            cx += plus.Width + gap;
                        }
                        SizeF ts = g.MeasureString(line.Chips[i], font, PointF.Empty, fmt);
                        var capRect = new RectangleF(cx, y + padY, ts.Width + 2 * capPadX, h - 2 * padY);
                        using (var capPath = ClickOverlay.RoundedRect(capRect, 6 * scale))
                            g.FillPath(capBrush, capPath);
                        g.DrawString(line.Chips[i], font, fgBrush, cx + capPadX, y + (h - ts.Height) / 2, fmt);
                        cx += capRect.Width;
                    }
                }

                if (line.Repeat > 1)
                {
                    string rep = "×" + line.Repeat;
                    SizeF rs = g.MeasureString(rep, small, PointF.Empty, fmt);
                    g.DrawString(rep, small, dimBrush, cx + gap, y + (h - rs.Height) / 2, fmt);
                }

                y += h + lineGap;
            }
        }

        int margin = (int)(s.BubbleMargin * scale);
        // Keep the bubble inside small recording regions.
        margin = Math.Min(margin, Math.Max(0, (anchor.Height - height) / 2));

        int left = s.BubblePosition switch
        {
            BubblePosition.BottomLeft => anchor.Left + margin,
            BubblePosition.BottomRight => anchor.Right - margin - width,
            _ => anchor.Left + (anchor.Width - width) / 2,
        };
        int top = s.BubblePosition == BubblePosition.TopCenter
            ? anchor.Top + margin
            : anchor.Bottom - margin - height;

        _window.Present(bmp, new Point(left, top));
    }

    /// <summary>Spaces at the edges would be invisible, so show them as "␣".</summary>
    private static string DisplayText(Line line)
    {
        string t = line.Text;
        int lead = t.Length - t.TrimStart(' ').Length;
        int trail = t.Length - t.TrimEnd(' ').Length;
        if (lead == t.Length) return new string('␣', t.Length);
        return new string('␣', lead) + t.Trim(' ') + new string('␣', trail);
    }

    private static Color Fade(Color c, float alpha) => Color.FromArgb((int)(c.A * alpha), c.R, c.G, c.B);

    public void Dispose() => _window.Dispose();
}
