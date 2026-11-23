# 一覧の先頭文字入力による項目移動と、閉じたタブの開き直し(Ctrl+Shift+T)の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_typeahead_tabs.ps1'
# 操作するフォルダは %TEMP%\kt_test_typeahead の中だけ。KT_Explorer が起動中なら中止する。
# 注意: Ctrl+W は、環境によってはキーがアプリへ届かないため、タブを閉じるには ✕ ボタンを使う。
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

    # ── タブ: 新規タブ → ✕ で閉じる → Ctrl+Shift+T で開き直し
    function Get-TabCount { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' }).Count }
    $t0 = Get-TabCount
    Set-Foreground
    Send-Keys '^t' 800
    $t1 = Get-TabCount
    Check 'Ctrl+T adds a tab' ($t1 -eq $t0 + 1)
    $last = (Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' })[-1]
    $A = $global:Kt.A
    $x = $last.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
    if (-not $x) { Stop-Test 'close button of the last tab not found' }
    $x.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $t2 = Get-TabCount
    Check 'close button closes the tab' ($t2 -eq $t0)
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
