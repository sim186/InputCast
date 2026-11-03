using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace InputCast;

/// <summary>Draws a coloured indicator at the cursor while a mouse button is held, and a ripple when it is released.</summary>
internal sealed class ClickOverlay : IDisposable
{
    private sealed class Effect
    {
        public required MouseButton Button;
        public required LayeredWindow Window;
        public Point Location;
        public bool Held;
        public long DownMs;
        public long ReleasedMs;
    }

    private readonly Func<Settings> _settings;
    private readonly Func<Point> _cursor;
    private readonly List<Effect> _effects = [];
    private readonly Stack<LayeredWindow> _pool = new();

    public ClickOverlay(Func<Settings> settings, Func<Point> cursor)
    {
        _settings = settings;
        _cursor = cursor;
    }

    public bool Busy => _effects.Count > 0;

    public void OnInput(InputEvent e, long nowMs)
    {
        if (!_settings().ShowClicks) return;

        if (e.Kind == InputKind.MouseDown)
        {
            // A missed "up" (e.g. hook was reinstalled mid-click) must not leave a stale indicator.
            foreach (var old in _effects.Where(x => x.Held && x.Button == e.Button))
                Release(old, old.Location, nowMs);

            _effects.Add(new Effect
            {
                Button = e.Button,
                Window = _pool.Count > 0 ? _pool.Pop() : new LayeredWindow(),
                Location = e.Location,
                Held = true,
                DownMs = nowMs,
            });
        }
        else if (e.Kind == InputKind.MouseUp)
        {
            var fx = _effects.LastOrDefault(x => x.Held && x.Button == e.Button);
            if (fx != null) Release(fx, e.Location, nowMs);
        }
    }

    private static void Release(Effect fx, Point at, long nowMs)
    {
        fx.Held = false;
        fx.Location = at;
        fx.ReleasedMs = nowMs;
    }

    public void Tick(long nowMs)
    {
        var s = _settings();
        Point cursor = _cursor();

        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var fx = _effects[i];
            float t = 0;

            if (fx.Held)
            {
                fx.Location = cursor;
                if (nowMs - fx.DownMs > 1000 && !IsPhysicallyDown(fx.Button))
                    Release(fx, cursor, nowMs);
            }

            if (!fx.Held)
            {
                t = (nowMs - fx.ReleasedMs) / (float)Math.Max(1, s.RippleDurationMs);
                if (t >= 1f)
                {
                    fx.Window.Hide();
                    _pool.Push(fx.Window);
                    _effects.RemoveAt(i);
                    continue;
                }
            }

            Render(fx, t, s);
        }
    }

    public void Clear()
    {
        foreach (var fx in _effects)
        {
            fx.Window.Hide();
            _pool.Push(fx.Window);
        }
        _effects.Clear();
    }

    private static void Render(Effect fx, float t, Settings s)
    {
        float scale = Native.ScaleAt(fx.Location);
        float r = s.ClickRadius * scale;
        float baseR = r * 0.55f;
        float maxR = r * 1.6f;
        float stroke = Math.Max(2f, 3f * scale);
        int half = (int)Math.Ceiling(maxR + stroke + 2);
        int labelSpace = s.ShowClickLabel ? (int)(18 * scale) : 0;
        int size = 2 * half + labelSpace;

        Color c = ButtonColor(fx.Button, s);

        using var bmp = LayeredWindow.NewCanvas(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            float cx = half, cy = half;

            if (fx.Held)
            {
                using var fill = new SolidBrush(Color.FromArgb(95, c));
                using var pen = new Pen(Color.FromArgb(235, c), stroke);
                using var outline = new Pen(Color.FromArgb(120, 0, 0, 0), 1f * scale);
                g.FillEllipse(fill, cx - baseR, cy - baseR, 2 * baseR, 2 * baseR);
                g.DrawEllipse(outline, cx - baseR - stroke / 2, cy - baseR - stroke / 2, 2 * baseR + stroke, 2 * baseR + stroke);
                g.DrawEllipse(pen, cx - baseR, cy - baseR, 2 * baseR, 2 * baseR);
            }
            else
            {
                float ease = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
                float rr = baseR + (maxR - baseR) * ease;
                int a = (int)(235 * (1f - t));
                using var fill = new SolidBrush(Color.FromArgb((int)(70 * (1f - t)), c));
                using var pen = new Pen(Color.FromArgb(a, c), stroke * (1f - 0.4f * t));
                g.FillEllipse(fill, cx - rr, cy - rr, 2 * rr, 2 * rr);
                g.DrawEllipse(pen, cx - rr, cy - rr, 2 * rr, 2 * rr);
            }

            if (s.ShowClickLabel && (fx.Held || t < 0.6f))
            {
                string label = KeyFormatter.ButtonLetter(fx.Button);
                int alpha = fx.Held ? 255 : (int)(255 * (1f - t / 0.6f));
                using var font = new Font("Segoe UI", 9f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                SizeF ts = g.MeasureString(label, font, PointF.Empty, StringFormat.GenericTypographic);
                float pw = ts.Width + 8 * scale, ph = ts.Height + 4 * scale;
                float px = cx + baseR * 0.75f, py = cy + baseR * 0.75f;
                using var pill = RoundedRect(new RectangleF(px, py, pw, ph), ph / 2);
                using var pillBrush = new SolidBrush(Color.FromArgb(alpha, c));
                using var textBrush = new SolidBrush(Color.FromArgb(alpha, 20, 20, 20));
                g.FillPath(pillBrush, pill);
                g.DrawString(label, font, textBrush, px + 4 * scale, py + 2 * scale, StringFormat.GenericTypographic);
            }
        }

        byte opacity = 255;
        fx.Window.Present(bmp, new Point(fx.Location.X - half, fx.Location.Y - half), opacity);
    }

    public static Color ButtonColor(MouseButton b, Settings s) => b switch
    {
        MouseButton.Left => Settings.ParseColor(s.LeftColor, Color.Gold),
        MouseButton.Right => Settings.ParseColor(s.RightColor, Color.Tomato),
        MouseButton.Middle => Settings.ParseColor(s.MiddleColor, Color.DeepSkyBlue),
        _ => Settings.ParseColor(s.XButtonColor, Color.MediumPurple),
    };

    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsPhysicallyDown(MouseButton b)
    {
        bool swapped = SystemInformation.MouseButtonsSwapped;
        int vk = b switch
        {
            MouseButton.Left => swapped ? 0x02 : 0x01,
            MouseButton.Right => swapped ? 0x01 : 0x02,
            MouseButton.Middle => 0x04,
            MouseButton.XButton1 => 0x05,
            _ => 0x06,
        };
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public void Dispose()
    {
        Clear();
        while (_pool.Count > 0) _pool.Pop().Dispose();
    }
}
