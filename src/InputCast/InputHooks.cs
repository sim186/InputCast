using System.Runtime.InteropServices;

namespace InputCast;

public enum MouseButton { Left, Right, Middle, XButton1, XButton2 }

public enum InputKind { KeyDown, KeyUp, MouseDown, MouseUp, Wheel, HWheel }

public readonly record struct InputEvent(
    InputKind Kind,
    Keys Key = Keys.None,
    uint ScanCode = 0,
    MouseButton Button = MouseButton.Left,
    Point Location = default,
    int WheelDelta = 0);

/// <summary>
/// Global low-level keyboard and mouse hooks. Events are only observed, never blocked or altered.
/// The hook callbacks do the bare minimum and defer processing to the UI message loop, so input
/// latency is never affected (and Windows does not silently drop a slow hook).
/// </summary>
internal sealed class InputHooks : IDisposable
{
    private readonly Native.LowLevelProc _keyboardProc;
    private readonly Native.LowLevelProc _mouseProc;
    private readonly SynchronizationContext _ui;
    private readonly Queue<InputEvent> _queue = new();
    private bool _drainPosted;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    /// <summary>Raised on the UI thread for every key/button/wheel event.</summary>
    public event Action<InputEvent>? Input;

    /// <summary>Latest cursor position reported by the mouse hook (physical pixels).</summary>
    public Point CursorPosition { get; private set; }

    public bool Installed => _keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero;

    public InputHooks()
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Must be created on the UI thread.");
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public void Install()
    {
        if (Installed) return;
        IntPtr module = Native.GetModuleHandle(null);
        _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        CursorPosition = Native.CursorPos();
    }

    public void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
        lock (_queue) _queue.Clear();
    }

    /// <summary>Injects an event as if it came from a hook (used by the demo mode).</summary>
    public void Simulate(InputEvent e)
    {
        if (e.Kind is InputKind.MouseDown or InputKind.MouseUp) CursorPosition = e.Location;
        Enqueue(e);
    }

    public void SimulateMove(Point p) => CursorPosition = p;

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            if (down || up)
                Enqueue(new InputEvent(down ? InputKind.KeyDown : InputKind.KeyUp, (Keys)data.vkCode, data.scanCode));
        }
        return Native.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var pt = new Point(data.pt.X, data.pt.Y);
            CursorPosition = pt;
            int hi = unchecked((short)(data.mouseData >> 16));
            MouseButton xb = hi == 2 ? MouseButton.XButton2 : MouseButton.XButton1;

            switch ((int)wParam)
            {
                case Native.WM_LBUTTONDOWN: Enqueue(new(InputKind.MouseDown, Button: MouseButton.Left, Location: pt)); break;
                case Native.WM_LBUTTONUP: Enqueue(new(InputKind.MouseUp, Button: MouseButton.Left, Location: pt)); break;
                case Native.WM_RBUTTONDOWN: Enqueue(new(InputKind.MouseDown, Button: MouseButton.Right, Location: pt)); break;
                case Native.WM_RBUTTONUP: Enqueue(new(InputKind.MouseUp, Button: MouseButton.Right, Location: pt)); break;
                case Native.WM_MBUTTONDOWN: Enqueue(new(InputKind.MouseDown, Button: MouseButton.Middle, Location: pt)); break;
                case Native.WM_MBUTTONUP: Enqueue(new(InputKind.MouseUp, Button: MouseButton.Middle, Location: pt)); break;
                case Native.WM_XBUTTONDOWN: Enqueue(new(InputKind.MouseDown, Button: xb, Location: pt)); break;
                case Native.WM_XBUTTONUP: Enqueue(new(InputKind.MouseUp, Button: xb, Location: pt)); break;
                case Native.WM_MOUSEWHEEL: Enqueue(new(InputKind.Wheel, Location: pt, WheelDelta: hi)); break;
                case Native.WM_MOUSEHWHEEL: Enqueue(new(InputKind.HWheel, Location: pt, WheelDelta: hi)); break;
            }
        }
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void Enqueue(InputEvent e)
    {
        lock (_queue)
        {
            _queue.Enqueue(e);
            if (_drainPosted) return;
            _drainPosted = true;
        }
        _ui.Post(_ => Drain(), null);
    }

    private void Drain()
    {
        while (true)
        {
            InputEvent e;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    _drainPosted = false;
                    return;
                }
                e = _queue.Dequeue();
            }
            try { Input?.Invoke(e); }
            catch { /* a rendering problem must never break input observation */ }
        }
    }

    public void Dispose() => Uninstall();
}
