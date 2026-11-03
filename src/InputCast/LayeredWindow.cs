using System.Drawing.Imaging;

namespace InputCast;

/// <summary>
/// A borderless, click-through, always-on-top window whose content is a per-pixel-alpha bitmap.
/// It never takes focus and never receives mouse input, but screen recorders capture it.
/// </summary>
internal sealed class LayeredWindow : NativeWindow, IDisposable
{
    private bool _visible;

    public LayeredWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "InputCast overlay",
            ClassName = null,
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
            Style = unchecked((int)Native.WS_POPUP),
            ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST |
                      Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
        });
    }

    public bool Visible => _visible;

    /// <summary>Shows <paramref name="bitmap"/> (32bpp ARGB) with its top-left corner at <paramref name="location"/>.</summary>
    public void Present(Bitmap bitmap, Point location, byte opacity = 255)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            // GetHbitmap with a fully transparent background yields a premultiplied 32-bit DIB.
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = Native.SelectObject(memDc, hBitmap);

            var size = new Native.SIZE { cx = bitmap.Width, cy = bitmap.Height };
            var src = new Native.POINT();
            var dst = new Native.POINT { X = location.X, Y = location.Y };
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = opacity,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };

            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBitmap != IntPtr.Zero)
            {
                Native.SelectObject(memDc, oldBitmap);
                Native.DeleteObject(hBitmap);
            }
            Native.DeleteDC(memDc);
        }

        if (!_visible)
        {
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            _visible = true;
        }

        // Re-assert z-order so we stay above other topmost windows (e.g. the recorder toolbar).
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void Hide()
    {
        if (!_visible) return;
        Native.ShowWindow(Handle, Native.SW_HIDE);
        _visible = false;
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }

    public static Bitmap NewCanvas(int width, int height) =>
        new(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
}
