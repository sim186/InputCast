using System.Drawing.Drawing2D;

namespace InputCast;

/// <summary>
/// A transient, draggable ghost of the key bubble used to pick a custom on-screen position.
/// Drag it anywhere, then click (without moving) to lock it in; Esc cancels, Enter also confirms.
/// When <see cref="Form.ShowDialog()"/> returns <see cref="DialogResult.OK"/>, <see cref="Form.Location"/>
/// is the chosen top-left for the real bubble.
/// </summary>
internal sealed class BubblePlacer : Form
{
    private readonly float _scale;
    private Point _dragStart;
    private Point _formStart;
    private bool _dragging;
    private bool _moved;

    public BubblePlacer(Point start, float scale)
    {
        _scale = Math.Max(1f, scale);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Opacity = 0.92;
        BackColor = Color.FromArgb(24, 24, 24);
        Cursor = Cursors.SizeAll;
        Size = new Size((int)(220 * _scale), (int)(84 * _scale));
        Location = start;
        Text = "Place key bubble";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(BackColor);

        float u = _scale;
        using (var border = new Pen(Color.FromArgb(90, 140, 200), 1.5f * u))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        using var font = new Font("Segoe UI Semibold", 15f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 10.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var cap = new SolidBrush(Color.FromArgb(58, 58, 58));
        using var fg = new SolidBrush(Color.White);
        using var dim = new SolidBrush(Color.FromArgb(150, 220, 220, 220));

        const string sample = "Ctrl  +  S";
        SizeF ss = g.MeasureString(sample, font);
        float sx = (Width - ss.Width) / 2f, sy = 14 * u;
        using (var capPath = Rounded(new RectangleF(sx - 9 * u, sy - 3 * u, ss.Width + 18 * u, ss.Height + 6 * u), 6 * u))
            g.FillPath(cap, capPath);
        g.DrawString(sample, font, fg, sx, sy);

        const string hint = "drag · click to lock · Esc cancels";
        SizeF hs = g.MeasureString(hint, small);
        g.DrawString(hint, small, dim, (Width - hs.Width) / 2f, Height - hs.Height - 9 * u);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _dragStart = MousePosition;
        _formStart = Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        Point p = MousePosition;
        int dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _moved = true;
        Location = new Point(_formStart.X + dx, _formStart.Y + dy);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (!_moved) Confirm();   // a click without dragging locks it in
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: DialogResult = DialogResult.Cancel; Close(); return true;
            case Keys.Enter: Confirm(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Confirm()
    {
        DialogResult = DialogResult.OK;
        Close();
    }
}
