# タブの複数段表示の画面テスト: 名前の長いタブを多数開き、「表示」メニューの「タブを複数段で表示」で、
# 1 段(あふれた分は画面の外)から、複数段(すべてのタブが画面の中)に切り替わること、オフで 1 段に戻ること、選択中のタブが変わらないこと。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_multirow_tabs.ps1'
# 操作するのは %TEMP%\kt_test_ui_multirow_tabs の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。実行中は、キーボードとマウスに触らないこと。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_multirow_tabs'
$names = 1..12 | ForEach-Object { 'long_folder_name_for_the_wrap_test_{0:00}' -f $_ }
foreach ($n in $names) { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }

function Get-Tabs { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' }) }
function Test-Selected($el) { try { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false } }
function Row-Count {
    # タブの上端(Y)が、ほぼ同じものを、同じ段とみなす
    $ys = @(Get-Tabs | ForEach-Object { [int]$_.Current.BoundingRectangle.Y } | Sort-Object -Unique)
    $rows = 0; $last = -1000
    foreach ($y in $ys) { if ($y - $last -gt 8) { $rows++ }; $last = $y }
    $rows
}
function All-Inside {
    $w = $global:Kt.Win.Current.BoundingRectangle
    @(Get-Tabs | Where-Object { $r = $_.Current.BoundingRectangle; $r.X -lt $w.X -or ($r.X + $r.Width) -gt ($w.X + $w.Width + 1) }).Count -eq 0
}
function Toggle-MultiRow {
    Set-Foreground
    $m = Find-AppMenuItem '表示(V)'
    if (-not $m) { Stop-Test "menu '表示(V)' not found" }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    [void](Wait-Until { [bool](Find-AppMenuItem 'タブを複数段で表示') } 3000 100)
    $item = Find-AppMenuItem 'タブを複数段で表示'
    if (-not $item) { Stop-Test 'menu item "タブを複数段で表示" not found' }
    $state = "$($item.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState)"
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 300
    Send-Keys '{ESC}' 0
    [void](Wait-Until { -not (Find-AppMenuItem 'タブを複数段で表示') } 2000 100)
    Send-Keys '{ESC}' 150
    $state
}

try {
    Start-KtApp
    Move-To "$d\$($names[0])"
    foreach ($n in $names | Select-Object -Skip 1) {
        Set-Foreground; Send-Keys '^t' 300
        Move-To "$d\$n"
    }
    $selectedPath = Get-PathText
    Start-Sleep -Milliseconds 500

    # ── 1) 最初は 1 段: タブが多く、あふれた分は、画面の外
    Check "1) single row at first (rows: $(Row-Count))" ((Row-Count) -eq 1)
    Check '1) with many long tabs, some of them are outside the window (they scroll)' (-not (All-Inside))

    # ── 2) 「タブを複数段で表示」をオン: 複数段になり、すべてのタブが画面の中に入る
    $before = Toggle-MultiRow
    Check "2) the menu item starts unchecked (was: $before)" ($before -eq 'Off')
    [void](Wait-Until { (Row-Count) -ge 2 } 3000 150)
    Check "2) tabs wrap onto several rows (rows: $(Row-Count))" ((Row-Count) -ge 2)
    Check '2) every tab is inside the window (no scrolling needed)' (All-Inside)
    Check '2) the number of tabs is unchanged' ((Get-Tabs).Count -ge 12)
    Check '2) the selected tab and the folder shown did not change' ((Get-PathText) -eq $selectedPath)

    # ── 3) 複数段のまま、別のタブを選べる(先頭のタブをクリック)
    $first = (Get-Tabs | Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X } | Select-Object -First 1)
    Click-Element $first
    Check "3) clicking a tab on the first row selects it ($(Get-PathText))" ((Get-PathText) -ne $selectedPath)

    # ── 4) オフに戻す: 1 段に戻る
    $state = Toggle-MultiRow
    Check "4) the menu item was checked before turning it off (was: $state)" ($state -eq 'On')
    [void](Wait-Until { (Row-Count) -eq 1 } 3000 150)
    Check "4) back to a single row (rows: $(Row-Count))" ((Row-Count) -eq 1)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
