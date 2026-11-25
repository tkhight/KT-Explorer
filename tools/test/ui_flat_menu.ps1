# メニューのフラットなデザインの画面テスト: メニューバーの見出し(普通・ホバー=淡い水色・開いている=濃い水色)、
# 開いたメニューの項目(ホバー・使えない項目の灰色・チェックの青いマーク・白い背景)を、画素の色で確かめる。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_flat_menu.ps1'
# 操作するのは %TEMP%\kt_test_ui_flat_menu の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。マウスは実際に動かす(テスト中は触らない)。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_flat_menu'
New-Item -ItemType Directory (Join-Path $d 'a') | Out-Null

function Pixel([int]$x, [int]$y) {
    $bmp = New-Object System.Drawing.Bitmap(1, 1)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
    $c = $bmp.GetPixel(0, 0); $bmp.Dispose(); $c
}
function Near($c, [int]$r, [int]$g, [int]$b, [int]$tol = 8) { ([Math]::Abs($c.R - $r) -le $tol) -and ([Math]::Abs($c.G - $g) -le $tol) -and ([Math]::Abs($c.B - $b) -le $tol) }
function Move-Mouse([int]$x, [int]$y) { Assert-AppForeground; [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 400 }
function Item-Rect([string]$name) { (Find-AppMenuItem $name).Current.BoundingRectangle }
function Open-Menu([string]$name, [string]$waitForItem) {
    Set-Foreground
    $m = Find-AppMenuItem $name
    if (-not $m) { Stop-Test "menu '$name' not found" }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    [void](Wait-Until { [bool](Find-AppMenuItem $waitForItem) } 3000 100)
}
function Close-Menus { Send-Keys '{ESC}' 200; Send-Keys '{ESC}' 200; Start-Sleep -Milliseconds 200 }
# 項目の矩形の中で、いちばん暗い画素の明るさ(文字の色の濃さ)
function Darkest([string]$name) {
    $r = Item-Rect $name
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size); $g.Dispose()
    $min = 255
    for ($x = 0; $x -lt $bmp.Width; $x++) { for ($y = 0; $y -lt $bmp.Height; $y++) { $c = $bmp.GetPixel($x, $y); $l = [int](0.3 * $c.R + 0.59 * $c.G + 0.11 * $c.B); if ($l -lt $min) { $min = $l } } }
    $bmp.Dispose(); $min
}

try {
    Start-KtApp
    Move-To "$d\a"
    # フォーカスを一覧から外す(パス欄にフォーカスが残ると、編集メニューは文字に働くため)
    Set-Foreground

    # ── 1) メニューバー: 普通は背景なし・ホバーで淡い水色
    $view = Item-Rect '表示(V)'
    Move-Mouse ([int]($view.X + $view.Width / 2)) ([int]($view.Y + $view.Height / 2))
    $hover = Pixel ([int]$view.X + 3) ([int]($view.Y + $view.Height / 2))
    Check "1) hovering a menu bar item turns it light blue (got $($hover.R),$($hover.G),$($hover.B))" (Near $hover 229 243 255 6)
    $file = Item-Rect 'ファイル(F)'
    $plain = Pixel ([int]$file.X + 3) ([int]($file.Y + $file.Height / 2))
    Check "2) an untouched menu bar item has no background (got $($plain.R),$($plain.G),$($plain.B); the bar is #FAFAFA)" (Near $plain 250 250 250 6)

    # ── 3) 開いているメニューの見出しは、濃い水色
    Open-Menu '編集(E)' '全て選択(A)'
    $edit = Item-Rect '編集(E)'
    $open = Pixel ([int]$edit.X + 3) ([int]($edit.Y + $edit.Height / 2))
    Check "3) the open menu's header is darker blue (got $($open.R),$($open.G),$($open.B))" (Near $open 204 232 255 8)

    # ── 4) 開いたメニュー: 使えない項目は薄い灰色の文字、使える項目は濃い文字
    $dis = Darkest 'やり直し(R)'; $ena = Darkest '全て選択(A)'
    Check "4) a disabled item has light gray text (darkest pixel $dis)" ($dis -ge 150)
    Check "4) an enabled item has dark text (darkest pixel $ena)" ($ena -lt 80)

    # ── 5) 項目にカーソルを乗せると、淡い水色(丸角の四角)
    $sel = Item-Rect '全て選択(A)'
    Move-Mouse ([int]($sel.X + $sel.Width / 2)) ([int]($sel.Y + $sel.Height / 2))
    $ih = Pixel ([int]$sel.X + 6) ([int]($sel.Y + $sel.Height / 2))
    Check "5) hovering a menu item turns it light blue (got $($ih.R),$($ih.G),$($ih.B))" (Near $ih 229 243 255 6)
    Close-Menus

    # ── 6) チェックのある項目には、左に、青いチェックマーク
    Open-Menu '表示(V)' 'ツリーをタブに連動'
    $chk = Item-Rect 'ツリーをタブに連動'
    $found = $false
    for ($x = [int]$chk.X + 2; $x -lt [int]$chk.X + 26; $x++) { for ($y = [int]$chk.Y + 4; $y -lt [int]$chk.Y + [int]$chk.Height - 4; $y++) {
        if (Near (Pixel $x $y) 11 92 173 40) { $found = $true; break } } if ($found) { break } }
    Check '6) a checked item shows a blue check mark at its left' $found
    $unchk = Item-Rect 'プレビューを表示'
    $foundU = $false
    for ($x = [int]$unchk.X + 2; $x -lt [int]$unchk.X + 26; $x++) { for ($y = [int]$unchk.Y + 4; $y -lt [int]$unchk.Y + [int]$unchk.Height - 4; $y++) {
        if (Near (Pixel $x $y) 11 92 173 40) { $foundU = $true; break } } if ($foundU) { break } }
    Check '6) an unchecked item shows no check mark' (-not $foundU)
    Close-Menus
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
