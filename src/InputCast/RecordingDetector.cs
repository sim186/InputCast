using System.Diagnostics;
using System.Text;

namespace InputCast;

/// <summary>
/// Detects a running ShareX screen recording.
///
/// ShareX 19+ shows a topmost tool window around the recorded region while recording
/// (title "ShareX - Screen recording", localized, e.g. "ShareX - Registrazione schermo").
/// Its layout is: 1 px border, the region, a 3 px gap, then a 42 DIP toolbar below.
/// Older ShareX versions record through an ffmpeg.exe child process; that is detected too
/// (without a region).
/// </summary>
internal sealed class RecordingDetector
{
    private const int BorderPixels = 1;
    private const int ToolbarGapPixels = 3;
    private const double ToolbarHeightDip = 42;

    private readonly Dictionary<uint, string> _processNames = [];
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private long _cacheResetMs;

    public bool IsRecording { get; private set; }
    public Rectangle? Region { get; private set; }

    public void Poll(Settings s, long nowMs)
    {
        if (nowMs - _cacheResetMs > 30_000)
        {
            _processNames.Clear();
            _cacheResetMs = nowMs;
        }

        IntPtr found = IntPtr.Zero;
        Native.RECT foundRect = default;
        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        var title = new StringBuilder(256);

        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;

            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOPMOST) == 0 || (ex & Native.WS_EX_TOOLWINDOW) == 0) return true;

            int len = Native.GetWindowTextLength(hwnd);
            if (len == 0) return true;
            title.Clear();
            title.EnsureCapacity(len + 1);
            Native.GetWindowText(hwnd, title, len + 1);
            string t = title.ToString();
            if (!s.RecorderTitlePrefixes.Any(p => t.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;

            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _ownPid) return true;
            string name = ProcessName(pid);
            if (!s.RecorderProcessNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))) return true;

            Native.GetWindowRect(hwnd, out var rect);
            // The region-selection overlay covers the whole desktop; that is not a recording.
            if (rect.ToRectangle() == virtualScreen) return true;

            found = hwnd;
            foundRect = rect;
            return false;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            IsRecording = true;
            Region = RegionFromWindow(foundRect.ToRectangle());
            return;
        }

        IsRecording = LegacyFfmpegRecording(s);
        Region = null;
    }

    private static Rectangle? RegionFromWindow(Rectangle w)
    {
        float scale = Native.ScaleAt(new Point(w.Left + w.Width / 2, w.Top + BorderPixels + 1));
        int toolbar = (int)Math.Ceiling(ToolbarHeightDip * scale);
        int top = w.Top + BorderPixels;
        int bottom = w.Bottom - toolbar - ToolbarGapPixels - BorderPixels;
        if (bottom - top < 20) return null;
        // The frame is centered horizontally; if the toolbar is wider than the region the exact
        // width is unknown, but the bubble is centered so the window width is a safe bound.
        return Rectangle.FromLTRB(w.Left + BorderPixels, top, w.Right - BorderPixels, bottom);
    }

    private string ProcessName(uint pid)
    {
        if (_processNames.TryGetValue(pid, out string? n)) return n;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            n = p.ProcessName;
        }
        catch
        {
            n = "";
        }
        _processNames[pid] = n;
        return n;
    }

    private static bool LegacyFfmpegRecording(Settings s)
    {
        foreach (var p in Process.GetProcessesByName("ffmpeg"))
        {
            using (p)
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (path != null && s.RecorderProcessNames.Any(n => path.Contains(n, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
                catch
                {
                    // Access denied (elevated process) - not ours.
                }
            }
        }
        return false;
    }
}
