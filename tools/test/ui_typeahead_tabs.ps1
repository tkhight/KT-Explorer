# 一覧の先頭文字入力による項目移動と、閉じたタブの開き直し(Ctrl+Shift+T)の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_typeahead_tabs.ps1'
# 操作するフォルダは %TEMP%\kt_test_typeahead の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 注意: Ctrl+W は、環境によってはキーがアプリへ届かないため、タブはタブの右クリックの「タブを閉じる」で閉じる(タブに ✕ は無い)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_typeahead'
foreach ($n in 'alpha', 'banana', 'bravo', 'cherry') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
$names = 'alpha', 'banana', 'bravo', 'cherry'
try {
    Start-KtApp
    Move-To $d

    $rows = Find-All 'DataItem'
    if ($rows.Count -ne 4) { Stop-Test "unexpected row count $($rows.Count) (expected 4: the test folders)" }
    function Get-SelectedName {
        $r = Find-All 'DataItem'
        for ($i = 0; $i -lt $r.Count; $i++) {
            if ($r[$i].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return $names[$i] }
        }
        return '(none)'
    }
    $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[0].SetFocus(); Start-Sleep -Milliseconds 400

    # ── 先頭文字入力(続けて入力すると絞り込まれ、少し間を置くとリセットされる)
    Send-Keys 'c' 500;  Check "typeahead 'c'  -> cherry" ((Get-SelectedName) -eq 'cherry')
    Start-Sleep -Milliseconds 1800
    Send-Keys 'br' 500; Check "typeahead 'br' -> bravo"  ((Get-SelectedName) -eq 'bravo')
    Start-Sleep -Milliseconds 1800
    Send-Keys 'ba' 500; Check "typeahead 'ba' -> banana" ((Get-SelectedName) -eq 'banana')

    # ── タブ: 新規タブ → 右クリックで閉じる → Ctrl+Shift+T で開き直し
    function Get-TabCount { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' }).Count }
    $t0 = Get-TabCount
    Set-Foreground
    Send-Keys '^t' 800
    $t1 = Get-TabCount
    Check 'Ctrl+T adds a tab' ($t1 -eq $t0 + 1)
    $last = (Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' })[-1]
    # タブの「✕」は無い。タブを右クリックして、「タブを閉じる」を選ぶ
    $rc = $last.Current.BoundingRectangle
    Assert-AppForeground
    [void][KtWin]::SetCursorPos([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2)); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200
    $close = Find-AppMenuItem 'タブを閉じる'
    if (-not $close) { Stop-Test 'the tab context menu (Close tab) was not found' }
    $close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $t2 = Get-TabCount
    Check 'the tab context menu closes the tab' ($t2 -eq $t0)
    Set-Foreground
    Send-Keys '^+t' 800
    $t3 = Get-TabCount
    Check 'Ctrl+Shift+T reopens the closed tab' ($t3 -eq $t0 + 1)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
