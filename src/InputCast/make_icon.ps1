# Generates appicon.ico (multi-size, PNG-compressed entries) for InputCast.
# Motif: a rounded keyboard with an amber click-ripple accent — matches the tray icon.
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $S / 16.0

    function RoundRect($x, $y, $w, $h, $r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        return $p
    }

    # Keyboard body
    $kb = RoundRect (1.2 * $u) (3.2 * $u) (13.6 * $u) (9.6 * $u) (2.2 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 42, 46, 54))), $kb)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 150, 156, 168), [float](0.9 * $u))), $kb)

    # Keys
    $key = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 218, 226))
    for ($row = 0; $row -lt 2; $row++) {
        for ($col = 0; $col -lt 4; $col++) {
            $kx = (3.0 + $col * 2.5) * $u
            $ky = (5.0 + $row * 2.4) * $u
            $g.FillPath($key, (RoundRect $kx $ky (1.6 * $u) (1.6 * $u) (0.45 * $u)))
        }
    }
    # Spacebar
    $g.FillPath($key, (RoundRect (3.0 * $u) (10.0 * $u) (7.1 * $u) (1.5 * $u) (0.45 * $u)))

    # Amber click-ripple accent (bottom-right), like a released click
    $cx = 11.7 * $u; $cy = 11.2 * $u
    $amber = [System.Drawing.Color]::FromArgb(255, 255, 193, 7)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, $amber))), ($cx - 3.2 * $u), ($cy - 3.2 * $u), (6.4 * $u), (6.4 * $u))
    $g.DrawEllipse((New-Object System.Drawing.Pen ($amber, [float](0.9 * $u))), ($cx - 3.1 * $u), ($cy - 3.1 * $u), (6.2 * $u), (6.2 * $u))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $amber), ($cx - 1.5 * $u), ($cy - 1.5 * $u), (3.0 * $u), (3.0 * $u))

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

$out = Join-Path $PSScriptRoot 'appicon.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = $sizes[$i]; if ($dim -ge 256) { $dim = 0 }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)        # width, height
    $bw.Write([byte]0); $bw.Write([byte]0)              # colors, reserved
    $bw.Write([uint16]1); $bw.Write([uint16]32)         # planes, bpp
    $bw.Write([uint32]$pngs[$i].Length)                 # bytes in resource
    $bw.Write([uint32]$offset)                          # offset
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $fs.Close()
Write-Host "Wrote $out ($((Get-Item $out).Length) bytes, $($sizes.Count) sizes)"
