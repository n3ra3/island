# Рисует значок приложения (чёрная «пилюля» с двумя глазками) и сохраняет его как app.ico в нескольких размерах.
# Запуск: powershell -File installer\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # «Пилюля» по центру
    $w = $s * 0.92; $h = $s * 0.46; $x = ($s - $w) / 2; $y = ($s - $h) / 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $h, $h, 90, 180)
    $path.AddArc($x + $w - $h, $y, $h, $h, 270, 180)
    $path.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::Black, $path)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([Math]::Max(1, $s / 64))
    $g.DrawPath($pen, $path)

    # Два глазка-капсулы
    $ew = [Math]::Max(1.5, $s * 0.075); $eh = $h * 0.52; $ey = $y + ($h - $eh) / 2
    $cx = $x + $h * 0.75
    foreach ($ex in @($cx, $cx + $ew * 2.1)) {
        $eye = New-Object System.Drawing.Drawing2D.GraphicsPath
        $eye.AddArc($ex, $ey, $ew, $ew, 180, 180)
        $eye.AddArc($ex, $ey + $eh - $ew, $ew, $ew, 0, 180)
        $eye.CloseFigure()
        $g.FillPath([System.Drawing.Brushes]::White, $eye)
    }
    # Оранжевый эквалайзер справа
    $bw = [Math]::Max(1, $s * 0.04); $bx = $x + $w - $h * 0.95
    $heights = 0.35, 0.7, 0.5, 0.85
    $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 159, 10))
    for ($i = 0; $i -lt 4; $i++) {
        $bh = $h * 0.5 * $heights[$i]
        $g.FillRectangle($orange, [float]($bx + $i * $bw * 1.9), [float]($y + ($h - $bh) / 2), [float]$bw, [float]$bh)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $bmp.Save("$PSScriptRoot\app-256.png") }
    $bmp.Dispose()
}

# Формат ICO: заголовок + каталог + PNG-картинки подряд
$out = New-Object System.IO.MemoryStream
$bw2 = New-Object System.IO.BinaryWriter $out
$bw2.Write([UInt16]0); $bw2.Write([UInt16]1); $bw2.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw2.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw2.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw2.Write([byte]0); $bw2.Write([byte]0); $bw2.Write([UInt16]1); $bw2.Write([UInt16]32)
    $bw2.Write([UInt32]$len); $bw2.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw2.Write($p) }
$bw2.Flush()
[System.IO.File]::WriteAllBytes("$PSScriptRoot\..\app.ico", $out.ToArray())
"app.ico готов"
