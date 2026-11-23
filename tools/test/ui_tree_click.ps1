# ツリーの項目の「名前の右側の空白」をクリックしても、その項目が選択される(タブがそのフォルダへ移る)ことの画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_tree_click.ps1'
# 操作するのは %TEMP%\kt_test_click の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_click'
foreach ($n in 'aaa', 'bbb') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
try {
    Start-KtApp

    # 先に aaa を開いて、ツリーを aaa まで展開する。そのあと親(kt_test_click)へ戻ると、ツリーでは、親の直後の項目が aaa になる
    Move-To (Join-Path $d 'aaa')
    Move-To $d
    Check 'moved back to the parent (path box shows it)' ((Get-PathText) -eq $d)

    $items = @(Find-All 'TreeItem')
    $parentIndex = -1
    for ($i = 0; $i -lt $items.Count; $i++) {
        $selected = $false
        try { $selected = $items[$i].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }
        if ($selected) { $parentIndex = $i; break }
    }
    if ($parentIndex -lt 0 -or $parentIndex + 1 -ge $items.Count) { Stop-Test 'could not find the selected tree item and its first child' }
    $child = $items[$parentIndex + 1]

    # 大きなツリーなので、子の項目を画面の中へスクロールしてから、位置を取る
    try { $child.GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
    Start-Sleep -Milliseconds 600
    $r = $child.Current.BoundingRectangle
    $wr = $global:Kt.Win.Current.BoundingRectangle
    if ([double]::IsInfinity($r.X) -or [double]::IsNaN($r.X) -or $r.Y -lt $wr.Y -or $r.Y -gt ($wr.Y + $wr.Height)) {
        Stop-Test "the child item is not inside the window (y=$($r.Y); window y=$($wr.Y)..$($wr.Y + $wr.Height))"
    }

    $tree = (Find-All 'Tree') | Select-Object -First 1
    $tr = $tree.Current.BoundingRectangle

    # 'aaa' の行の、名前より右の空白を、実際のマウスでクリックする。
    # 行の幅は、横にスクロールできるため、ツリーの見えている範囲より広いことがある。見えている範囲の右端の少し内側を狙う。
    $x = [int]([math]::Min($r.X + $r.Width - 25, $tr.X + $tr.Width - 30))
    $y = [int]($r.Y + $r.Height / 2)
    if ($x -lt $r.X + 55) { Stop-Test 'the visible part of the row is too narrow to tell a blank click from a click on the name' }
    Write-Output "   (child row: x=$([int]$r.X) w=$([int]$r.Width); clicking the blank area at $x,$y)"
    Write-Output "   (tree: x=$([int]$tr.X) y=$([int]$tr.Y) w=$([int]$tr.Width) h=$([int]$tr.Height); window: y=$([int]$wr.Y) h=$([int]$wr.Height))"
    $at = [Windows.Automation.AutomationElement]::FromPoint([System.Windows.Point]::new($x, $y))
    Write-Output "   (element at the click point: type=$($at.Current.ControlType.ProgrammaticName) pid=$($at.Current.ProcessId) (app pid=$($global:Kt.Proc.Id)))"
    Click-Point $x $y
    $selNow = Get-SelectedTreeItem
    $sr = if ($selNow) { $selNow.Current.BoundingRectangle } else { $null }
    Write-Output "   (after the click: path box='$(Get-PathText)'; selected row y=$(if ($sr) { [int]$sr.Y } else { 'none' }) (child row y=$([int]$r.Y), parent row y=$([int]$items[$parentIndex].Current.BoundingRectangle.Y)))"
    Check 'clicking the blank area to the right of the name selects that item' ((Get-PathText) -eq (Join-Path $d 'aaa'))
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
