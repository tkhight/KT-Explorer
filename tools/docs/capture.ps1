# 使い方(docs/usage.html)に載せる画面を撮り直す。出力は docs\images\*.png。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\docs\capture.ps1'
# 普段の設定やフォルダが写り込まないように、専用の状態(KT_EXPLORER_STATE_DIR)と架空のフォルダで起動する。
# 架空のフォルダは %TEMP%\kt_test_docs の中に作り、一時的なドライブ文字(subst)に割り当てて、ツリーもそこだけにする。
# 起動中の KT_Explorer は閉じる。実際のマウスを使うので、撮影中は触らない。撮った画像は、必ず目で確認すること。
. (Join-Path $PSScriptRoot '..\test\common.ps1')
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KtShot {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][KtShot]::SetProcessDPIAware()

$out = (New-Item -ItemType Directory (Join-Path $PSScriptRoot '..\..\docs\images') -Force).FullName
$work = New-TestDir 'kt_test_docs'
$sample = Join-Path $work 'sample'
$drive = 'K', 'L', 'M', 'N', 'P', 'Q' | Where-Object { -not (Test-Path "$($_):\") } | Select-Object -First 1
if (-not $drive) { Write-Output 'ABORT: no free drive letter'; exit 2 }
$root = "$($drive):"

# ── 架空のフォルダ
foreach ($f in 'ProjectA\images', 'ProjectA\docs', 'ProjectB\images', '資料\会議') { New-Item -ItemType Directory (Join-Path $sample $f) -Force | Out-Null }
$files = [ordered]@{
    'ProjectA\images\banner.jpg' = 48000; 'ProjectA\images\hero.png' = 210000; 'ProjectA\images\icon.png' = 3200; 'ProjectA\images\logo.png' = 12000
    'ProjectA\images\photo01.jpg' = 640000; 'ProjectA\images\photo02.jpg' = 580000; 'ProjectA\images\readme.txt' = 900; 'ProjectA\images\sketch.png' = 76000
    'ProjectA\docs\仕様書.txt' = 4200; 'ProjectA\docs\議事録.txt' = 2100; 'ProjectB\images\top.png' = 88000; 'ProjectB\images\bg.jpg' = 150000
    '資料\見積.txt' = 1500; '資料\予定.txt' = 700
}
foreach ($k in $files.Keys) { [System.IO.File]::WriteAllBytes((Join-Path $sample $k), (New-Object byte[] $files[$k])) }

# ── 撮影専用の状態
$tabs = "$root\ProjectA\images", "$root\ProjectB\images", "$root\ProjectA\docs", "$root\資料"
$state = @{
    SyncTreeWithTab = $true; CaptureExplorer = $false; CloseToTray = $false; Left = 40; Top = 30; Width = 1040; Height = 640; SelectedGroupIndex = 0
    Groups = @(
        @{ Name = 'デフォルト'; IsDefault = $true; TabPaths = $tabs; SelectedIndex = 0; TabSorts = @(@{}, @{}, @{ IsPinned = $true; Color = '#E5484D' }, @{}) }
        @{ Name = '制作'; Color = '#30A46C'; TabPaths = @("$root\ProjectA"); TabSorts = @(@{}) }
        @{ Name = '資料'; Color = '#1B9BD6'; TabPaths = @("$root\資料"); TabSorts = @(@{}) }
    )
}
$stateDir = (New-Item -ItemType Directory (Join-Path $work 'state')).FullName
Set-Content -LiteralPath (Join-Path $stateDir 'state.json') -Value ($state | ConvertTo-Json -Depth 6) -Encoding utf8NoBOM

# ── 撮影の道具
function Frame {   # ウィンドウの、実際に見えている範囲(影や、後ろの画面を含めない)
    $r = New-Object KtShot+RECT
    [void][KtShot]::DwmGetWindowAttribute([IntPtr]$global:Kt.Win.Current.NativeWindowHandle, 9, [ref]$r, 16)
    [System.Drawing.Rectangle]::FromLTRB($r.L + 1, $r.T + 1, $r.R - 1, $r.B - 1)
}
function Grab([System.Drawing.Rectangle]$area) {
    Assert-AppForeground
    $bmp = New-Object System.Drawing.Bitmap($area.Width, $area.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($area.X, $area.Y, 0, 0, $bmp.Size); $g.Dispose()
    $bmp
}
function Mark($bmp, [System.Drawing.Rectangle]$area, [int]$number, [int]$x, [int]$y) {   # 画面の位置(x, y)に、番号の丸印
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'
    $cx = $x - $area.X; $cy = $y - $area.Y
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 74, 25))), ($cx - 13), ($cy - 13), 26, 26)
    $g.DrawEllipse((New-Object System.Drawing.Pen([System.Drawing.Color]::White, 2)), ($cx - 13), ($cy - 13), 26, 26)
    $font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString("$number", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(($cx - 13), ($cy - 12), 26, 26)), $fmt)
    $g.Dispose()
}
function Save($bmp, [string]$name) { $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); Write-Output "saved $name" }
function Rc($el) { $el.Current.BoundingRectangle }
function Items([string]$type) { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like "*$type*" } | Sort-Object { (Rc $_).X }) }
function Name-Text([string]$name) {   # 一覧の中の、その名前の文字(ツリーより右)
    $left = (Frame).X + 220
    @(Find-All 'Text' | Where-Object { $_.Current.Name -eq $name -and (Rc $_).X -gt $left -and (Rc $_).Y -gt ((Frame).Y + 170) } | Sort-Object { (Rc $_).X })
}
function Park { [void][KtWin]::SetCursorPos(((Frame).Right - 30), ((Frame).Bottom - 12)); Start-Sleep -Milliseconds 700 }

try {
    subst $root $sample
    $env:KT_EXPLORER_STATE_DIR = $stateDir
    $env:KT_EXPLORER_TREE_ROOT = "$root\"
    Start-KtApp
    Start-Sleep -Milliseconds 1500
    if ((Get-PathText) -ne "$root\ProjectA\images") { Stop-Test "the demo state was not used (path box: '$(Get-PathText)')" }
    Park

    # ── 1) 全体と各部の名前
    $f = Frame; $bmp = Grab $f
    $menu = Rc (Find-AppMenuItem 'ヘルプ(H)'); $path = Rc (Find-ById 'PathBox'); $group = Rc (Items 'TabGroup')[-1]
    $tab = Rc (Items 'FolderTab')[-1]; $tree = Rc (Find-All 'TreeItem' | Select-Object -First 1); $name = Rc (Name-Text 'readme.txt')[0]; $pane = Rc (Find-ById 'SpecifyPaneButton')
    Mark $bmp $f 1 ([int]($menu.Right + 24)) ([int]($menu.Y + $menu.Height / 2))
    Mark $bmp $f 2 ([int]($pane.X - 90)) ([int]($pane.Y + $pane.Height / 2))
    Mark $bmp $f 3 ([int]($path.Right - 40)) ([int]($path.Y + $path.Height / 2))
    Mark $bmp $f 4 ([int]($group.Right + 24)) ([int]($group.Y + $group.Height / 2))
    Mark $bmp $f 5 ([int]($f.X + 150)) ([int]($tree.Y + 200))
    Mark $bmp $f 6 ([int]($tab.Right + 24)) ([int]($tab.Y + $tab.Height / 2))
    Mark $bmp $f 7 ([int]($name.Right + 120)) ([int]($name.Y + 40))
    Save $bmp 'overview.png'

    # ── 2) グループタブとフォルダタブ
    $top = (Rc (Items 'TabGroup')[0]).Y - 6
    $bottom = (Rc (Items 'FolderTab')[0]).Bottom + 34
    $area = [System.Drawing.Rectangle]::FromLTRB($f.X, [int]$top, $f.Right, [int]$bottom)
    $bmp = Grab $area
    $g1 = Rc (Items 'TabGroup')[1]; $t = Items 'FolderTab'
    Mark $bmp $area 1 ([int]((Rc (Items 'TabGroup')[-1]).Right + 20)) ([int]($g1.Y + $g1.Height / 2))
    Mark $bmp $area 2 ([int]((Rc $t[1]).X + (Rc $t[1]).Width / 2)) ([int]((Rc $t[1]).Bottom + 17))
    Mark $bmp $area 3 ([int]((Rc $t[2]).X + (Rc $t[2]).Width / 2)) ([int]((Rc $t[2]).Bottom + 17))
    Save $bmp 'tabs.png'

    # ── 3) 範囲選択: 左の余白からドラッグして、押したまま撮る
    $a = Rc (Name-Text 'hero.png')[0]; $b = Rc (Name-Text 'photo01.jpg')[0]
    $x1 = [int]($a.X - 28); $y1 = [int]($a.Y + $a.Height / 2); $x2 = [int]($a.X + 330); $y2 = [int]($b.Y + $b.Height / 2)
    Assert-AppForeground
    [void][KtWin]::SetCursorPos($x1, $y1); Start-Sleep -Milliseconds 400
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    foreach ($i in 1..8) { [void][KtWin]::SetCursorPos([int]($x1 + ($x2 - $x1) * $i / 8), [int]($y1 + ($y2 - $y1) * $i / 8)); Start-Sleep -Milliseconds 60 }
    Start-Sleep -Milliseconds 500
    $listLeft = [int]($a.X - 64); $listTop = [int]((Rc (Items 'FolderTab')[0]).Bottom)
    $area = [System.Drawing.Rectangle]::FromLTRB($listLeft, $listTop, [Math]::Min($f.Right, $listLeft + 620), [int]($b.Bottom + 90))
    $bmp = Grab $area
    [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 500
    Mark $bmp $area 1 ($x1 - 21) $y1
    Mark $bmp $area 2 ($x2 + 24) $y2
    Save $bmp 'marquee.png'

    # ── 4) 右クリックメニュー
    Click-Point ([int]($a.X + 200)) ([int]($b.Bottom + 150))   # 一覧の下の空きをクリックして、選択を解除
    $r = Rc (Name-Text 'hero.png')[0]
    [void][KtWin]::SetCursorPos([int]($r.X + 20), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
    [void](Wait-Until { [bool](Find-AppMenuItem 'プロパティ(R)') } 4000 150); Start-Sleep -Milliseconds 700
    # メニューの枠は、項目の親の要素
    $popup = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent((Find-AppMenuItem 'プロパティ(R)'))
    if (-not $popup) { Stop-Test 'the context menu was not found' }
    [void][KtWin]::SetCursorPos(($f.Right - 30), ($f.Bottom - 12)); Start-Sleep -Milliseconds 500
    $m = Rc $popup
    $area = [System.Drawing.Rectangle]::Intersect([System.Drawing.Rectangle]::FromLTRB([int]$m.X + 9, [int]$m.Y + 3, [int]$m.Right - 9, [int]$m.Bottom - 7), $f)
    Save (Grab $area) 'context-menu.png'
    Send-Keys '{ESC}' 600

    # ── 5) 絞込
    $box = Find-ById 'FilterBox'
    $box.SetFocus(); Start-Sleep -Milliseconds 300
    Send-Text 'size:>50KB'
    Start-Sleep -Milliseconds 1200
    Park
    $f = Frame
    $area = [System.Drawing.Rectangle]::FromLTRB($f.X, [int]((Rc (Find-ById 'PathBox')).Y - 8), $f.Right, [int]($f.Y + 420))
    $bmp = Grab $area
    $fr = Rc (Find-ById 'FilterBox')
    Mark $bmp $area 1 ([int]($fr.X - 2)) ([int]($fr.Bottom + 4))
    Save $bmp 'filter.png'
    $box.SetFocus(); Send-Keys '{ESC}' 700

    # ── 6) ペイン表示
    Click-Element (Items 'FolderTab')[0]
    (Find-ById 'Panes2Button').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-Until { (Name-Text 'hero.png').Count -eq 2 } 4000 200)
    $r = Rc (Name-Text 'readme.txt')[1]; Click-Point ([int]($r.Right + 150)) ([int]($r.Y + 70))
    Move-To "$root\ProjectB\images"
    Park
    $f = Frame
    $area = [System.Drawing.Rectangle]::FromLTRB($f.X, [int]((Rc (Items 'TabGroup')[0]).Y - 6), $f.Right, [int]($f.Y + 400))
    Save (Grab $area) 'panes.png'
}
finally {
    Stop-KtApp
    $env:KT_EXPLORER_STATE_DIR = $null
    $env:KT_EXPLORER_TREE_ROOT = $null
    subst $root /d | Out-Null
    Remove-TestDir $work
}
