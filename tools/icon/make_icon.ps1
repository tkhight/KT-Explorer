# アプリのアイコン(app.ico)を作る: 水色のフォルダを、左右 2 つのペインに分けた図案(2 ペイン表示を表す)。
# 16 〜 256px の複数の大きさを、PNG 形式で 1 つの .ico にまとめる。小さい大きさ(16・24px)は、行の線を省いた簡略版を使う。
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\icon\make_icon.ps1'
# 出力: src\KT_Explorer\Resources\app.ico と、確認用の tools\icon\app_preview.png
Add-Type -AssemblyName System.Drawing

function Round-Rect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure()
    $p
}
function Brush([string]$hex) { New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($hex)) }
function Grad([float]$x, [float]$y, [float]$w, [float]$h, [string]$c1, [string]$c2) {
    New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.RectangleF($x, $y, $w, $h)), [System.Drawing.ColorTranslator]::FromHtml($c1), [System.Drawing.ColorTranslator]::FromHtml($c2), 90)
}

# 256px の原画。lines=$false なら、ペインの中の行の線を描かない(小さい大きさで潰れないように)
function Draw-Master([bool]$lines) {
    $bmp = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $g.FillPath((Grad 0 0 256 256 '#5CC0F5' '#1B8AD6'), (Round-Rect 14 52 228 176 20))   # フォルダの本体
    $g.FillPath((Brush '#2B9BE3'), (Round-Rect 14 32 108 50 14))                          # 左上のタブ
    $g.FillPath((Brush '#FFFFFF'), (Round-Rect 34 92 90 116 10))                          # 左のペイン
    $g.FillPath((Brush '#FFFFFF'), (Round-Rect 134 92 90 116 10))                         # 右のペイン
    if ($lines) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#1B8AD6'), 8); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        foreach ($x0 in 46, 146) { foreach ($y in 116, 140, 164) { $g.DrawLine($pen, $x0, $y, $x0 + 66, $y) } }
    }
    $g.Dispose()
    $bmp
}
function Scale([System.Drawing.Bitmap]$src, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size); $g.Dispose()
    $bmp
}

$full = Draw-Master $true
$simple = Draw-Master $false
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $master = if ($s -le 24) { $simple } else { $full }
    $bmp = if ($s -eq 256) { $master } else { Scale $master $s }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @($s, $ms.ToArray())
}

# ICO: ヘッダ(6) + 各画像の項目(16)+ PNG のデータ
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = $p[0]; $data = $p[1]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($p in $pngs) { $w.Write($p[1]) }
$w.Flush()

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dir = Join-Path $repo 'src\KT_Explorer\Resources'
New-Item -ItemType Directory -Force $dir | Out-Null
$icoPath = Join-Path $dir 'app.ico'
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
"saved: $icoPath ($($out.Length) bytes, sizes: $($sizes -join ', '))"

# 確認用: 明るい背景・暗い背景に、各サイズを並べる
$sheet = New-Object System.Drawing.Bitmap(560, 330)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.InterpolationMode = 'NearestNeighbor'
$g.FillRectangle((Brush '#F3F3F3'), 0, 0, 560, 165); $g.FillRectangle((Brush '#2B2B2B'), 0, 165, 560, 165)
foreach ($row in 0, 1) {
    $x = 10; $y0 = $row * 165 + 10
    foreach ($p in $pngs) {
        $s = $p[0]
        $img = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream(, $p[1])))
        $g.DrawImage($img, $x, $y0 + (128 - [Math]::Min($s, 128)), [Math]::Min($s, 128), [Math]::Min($s, 128))
        $x += [Math]::Min($s, 128) + 10
        if ($x -gt 520) { break }
    }
}
$g.Dispose()
$sheet.Save((Join-Path $PSScriptRoot 'app_preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
