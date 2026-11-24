# ツリーとタブのアイコンの画面テスト: タブの左と、ツリーのノードの左に、フォルダのアイコン(黄色)が出ること、
# ジャンクションのフォルダのタブには、リンクの矢印(オーバーレイ)が重なり、「表示」メニューのオフで消えること(画素を比べる)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_navicon.ps1'
# 操作するのは %TEMP%\kt_test_ui_navicon の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。実行中は、キーボードとマウスに触らないこと。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_navicon'
foreach ($n in 'plain', 'target') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
cmd /c "mklink /J `"$d\junc`" `"$d\target`"" | Out-Null

function Test-Selected($el) { try { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false } }
function Get-SelectedTab { Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' } | Where-Object { Test-Selected $_ } | Select-Object -First 1 }
# 領域(画面の座標)を撮る
function Grab([int]$x, [int]$y, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
    $bmp
}
# フォルダのアイコンの黄色(R が大きく、G がそれに次ぎ、B が小さい)の画素があるか
function Has-Yellow($bmp) {
    for ($x = 0; $x -lt $bmp.Width; $x++) { for ($y = 0; $y -lt $bmp.Height; $y++) {
        $c = $bmp.GetPixel($x, $y); if ($c.R -gt 215 -and $c.G -gt 165 -and $c.B -lt 140) { return $true }
    } }
    $false
}
function Has-Colored($bmp) {
    for ($x = 0; $x -lt $bmp.Width; $x++) { for ($y = 0; $y -lt $bmp.Height; $y++) {
        $c = $bmp.GetPixel($x, $y); $mx = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)); $mn = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
        if (($mx - $mn) -gt 80) { return $true }
    } }
    $false
}
function Diff-Pixels($a, $b) {
    $n = 0
    for ($x = 0; $x -lt $a.Width; $x++) { for ($y = 0; $y -lt [Math]::Min($a.Height, $b.Height); $y++) {
        $p = $a.GetPixel($x, $y); $q = $b.GetPixel($x, $y)
        if ([Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B) -gt 60) { $n++ }
    } }
    $n
}
function Tab-IconRegion { $rc = (Get-SelectedTab).Current.BoundingRectangle; Grab ([int]$rc.X) ([int]$rc.Y) 30 ([int][Math]::Min($rc.Height, 26)) }
function Toggle-Overlay {
    Set-Foreground
    $m = Find-AppMenuItem '表示(V)'
    if (-not $m) { Stop-Test "menu '表示(V)' not found" }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 900
    $item = Find-AppMenuItem 'オーバーレイアイコンを表示'
    if (-not $item) { Stop-Test 'menu item "オーバーレイアイコンを表示" not found' }
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    Send-Keys '{ESC}' 500; Send-Keys '{ESC}' 500
}

try {
    Start-KtApp
    Move-To "$d\plain"
    Start-Sleep -Milliseconds 1500

    # ── 1) タブの左に、フォルダのアイコン(黄色)が出ている
    $tabIcon = Tab-IconRegion
    Check '1) the tab shows a folder icon (yellow pixels at its left)' (Has-Yellow $tabIcon)

    # ── 2) ツリーのノードの左に、アイコンが出ている(画面に見えている最初のノード=デスクトップなどのルート。展開の矢印とアイコンの範囲に、色のついた画素がある)
    $win = $global:Kt.Win.Current.BoundingRectangle
    $node = Find-All 'TreeItem' | Where-Object { $r = $_.Current.BoundingRectangle; $r.Y -gt $win.Y -and $r.Y -lt ($win.Y + $win.Height) } | Select-Object -First 1
    if (-not $node) { Stop-Test 'a visible tree node was not found' }
    $nr = $node.Current.BoundingRectangle
    $treeRegion = Grab ([int]$nr.X) ([int]$nr.Y) 38 ([int][Math]::Min($nr.Height, 22))
    Check '2) a tree node shows an icon (colored pixels left of the name)' (Has-Colored $treeRegion)

    # ── 3) ジャンクションのフォルダへ移ると、タブのアイコンにリンクの矢印が重なる。オーバーレイをオフにすると、消える
    Move-To "$d\junc"
    Start-Sleep -Milliseconds 2500
    $junOn = Tab-IconRegion
    Toggle-Overlay
    Start-Sleep -Milliseconds 1200
    $junOff = Tab-IconRegion
    $dJ = Diff-Pixels $junOn $junOff
    Check "3) turning the overlay off removes the link arrow from the tab's icon ($dJ pixels differ)" ($dJ -ge 6)
    Toggle-Overlay
    Start-Sleep -Milliseconds 2500
    $junBack = Tab-IconRegion
    $dB = Diff-Pixels $junOn $junBack
    Check "3) turning it on again brings the arrow back ($dB pixels differ from the first)" ($dB -le 2)

    # ── 4) 普通のフォルダのタブは、オン/オフで変わらない
    Move-To "$d\plain"
    Start-Sleep -Milliseconds 1500
    $plainOn = Tab-IconRegion
    Toggle-Overlay; Start-Sleep -Milliseconds 1200
    $plainOff = Tab-IconRegion
    $dP = Diff-Pixels $plainOn $plainOff
    Check "4) a normal folder's tab icon does not change with the setting ($dP pixels differ)" ($dP -le 2)
    Toggle-Overlay
}
finally {
    Stop-KtApp
    cmd /c "rmdir `"$d\junc`"" | Out-Null
    Remove-TestDir $d
}
Show-Result
