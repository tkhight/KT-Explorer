# 指定ペインの作成ダイアログの画面テスト: 「既存タブから選択」(チェックを 2〜3 個付けて「作成」)と、「フォルダを指定」(2〜3 個入力して「作成」)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_pane_create.ps1'
# 操作するのは %TEMP%\kt_test_ui_pane_create の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_pane_create'
foreach ($n in 'a', 'b', 'c') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }

function Find-Dialog([string]$title) {
    $A = $global:Kt.A
    for ($i = 0; $i -lt 20; $i++) {
        $byName = New-Object Windows.Automation.PropertyCondition($A::NameProperty, $title)
        $w = $global:Kt.Root.FindFirst('Children', (New-Object Windows.Automation.AndCondition($global:Kt.PidCond, $byName)))
        if (-not $w) { $w = $global:Kt.Win.FindFirst('Descendants', $byName) }
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    }
}
function Dlg-Button($dlg, [string]$name) {
    $A = $global:Kt.A
    $dlg.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)),
        (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)))))
}
function Dlg-ById($dlg, [string]$id) {
    $A = $global:Kt.A
    $dlg.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)))
}
function Dlg-CheckBoxes($dlg) {
    $A = $global:Kt.A
    @($dlg.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::CheckBox))))
}
function Open-SpecifyMenu([string]$item) {
    Set-Foreground
    $b = Find-All 'Button' | Where-Object { $_.Current.Name -like '指定ペイン*' } | Select-Object -First 1
    if (-not $b) { Stop-Test 'the specify-pane button was not found' }
    $b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900
    $m = Find-AppMenuItem $item
    if (-not $m) { Stop-Test "menu item '$item' not found" }
    $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200
}
function Pane-Tab-Count { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*PaneTab*' }).Count }
function Press($button) { $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900 }
function Is-On($cb) { $cb.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' }

try {
    Start-KtApp
    # 準備: 3 つのタブ(a、b、c)。最後に開いた c のタブが選択された状態にする
    Move-To "$d\a"
    Set-Foreground; Send-Keys '^t' 700; Move-To "$d\b"
    Set-Foreground; Send-Keys '^t' 700; Move-To "$d\c"
    $panes0 = Pane-Tab-Count

    # ── 1) 既存タブから選択: 最初は、いま開いているタブ(c)だけにチェック → 「作成」は押せない
    Open-SpecifyMenu '既存タブから選択…'
    $dlg = Find-Dialog '指定ペイン: 既存タブから選択'
    if (-not $dlg) { Stop-Test 'the tab-selection dialog did not open' }
    $create = Dlg-Button $dlg '作成'
    $boxes = Dlg-CheckBoxes $dlg
    $mine = @($boxes | Where-Object { $_.Current.Name -like "*kt_test_ui_pane_create\a*" -or $_.Current.Name -like "*kt_test_ui_pane_create\b*" -or $_.Current.Name -like "*kt_test_ui_pane_create\c*" })
    Check "1) the dialog lists the open tabs with check boxes (test tabs: $($mine.Count))" ($mine.Count -ge 3)
    $cb = @{}; foreach ($x in $mine) { foreach ($k in 'a', 'b', 'c') { if ($x.Current.Name -like "*kt_test_ui_pane_create\$k  *" -or $x.Current.Name -like "*kt_test_ui_pane_create\$k") { $cb[$k] = $x } } }
    Check '1) only the current tab (c) is checked at first, and "作成" is disabled' ((Is-On $cb['c']) -and (-not (Is-On $cb['a'])) -and (-not $create.Current.IsEnabled))

    # ── 2) もう 1 つチェック(2 個)→ 「作成」が押せる
    $cb['a'].GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 600
    Check '2) with two tabs checked, "作成" is enabled' ($create.Current.IsEnabled)

    # ── 3) 3 個チェック → 「作成」は押せて、残りのタブ(4 個目)はチェックできなくなる
    $cb['b'].GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 600
    $others = @($boxes | Where-Object { -not (Is-On $_) })
    Check '3) with three checked, "作成" is enabled' ($create.Current.IsEnabled)
    Check "3) with three checked, the unchecked ones (if any) are disabled ($($others.Count))" ((@($others | Where-Object { $_.Current.IsEnabled }).Count) -eq 0)

    # ── 4) 3 個 → 1 つ外して 2 個にして、「作成」: ペインタブができる(a と c の 2 ペイン)
    $cb['b'].GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 600
    Press $create
    Start-Sleep -Milliseconds 800
    Check "4) pressing 作成 adds a pane tab ($panes0 -> $(Pane-Tab-Count))" ((Pane-Tab-Count) -eq $panes0 + 1)
    Check '4) the dialog closed' (-not (Find-Dialog '指定ペイン: 既存タブから選択' | Where-Object { $_ }))

    # ── 5) フォルダを指定: 1 つだけでは「作成」が押せない
    Open-SpecifyMenu 'フォルダを指定…'
    $dlg = Find-Dialog '指定ペイン: フォルダを指定'
    if (-not $dlg) { Stop-Test 'the folder dialog did not open' }
    $create = Dlg-Button $dlg '作成'
    $f1 = Dlg-ById $dlg 'PaneFolder1'; $f2 = Dlg-ById $dlg 'PaneFolder2'; $f3 = Dlg-ById $dlg 'PaneFolder3'
    Check '5) the folder dialog has three boxes, and "作成" is disabled at first' ($f1 -and $f2 -and $f3 -and (-not $create.Current.IsEnabled))
    $f1.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("$d\a"); Start-Sleep -Milliseconds 500
    Check '5) one folder is not enough ("作成" stays disabled)' (-not $create.Current.IsEnabled)

    # ── 6) 2 つ目 → 押せる。存在しない 3 つ目 → 押せなくなる。消すと、また押せる
    $f2.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("$d\b"); Start-Sleep -Milliseconds 500
    Check '6) two folders -> "作成" is enabled' ($create.Current.IsEnabled)
    $f3.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("$d\nope"); Start-Sleep -Milliseconds 500
    Check '6) a folder that does not exist disables "作成"' (-not $create.Current.IsEnabled)
    $f3.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("$d\c"); Start-Sleep -Milliseconds 500
    Check '6) three existing folders -> "作成" is enabled' ($create.Current.IsEnabled)

    # ── 7) 「作成」: ペインタブが、もう 1 つできる
    $before = Pane-Tab-Count
    Press $create
    Start-Sleep -Milliseconds 800
    Check "7) pressing 作成 adds another pane tab ($before -> $(Pane-Tab-Count))" ((Pane-Tab-Count) -eq $before + 1)

    # ── 8) キャンセルでは、何も作られない
    Open-SpecifyMenu 'フォルダを指定…'
    $dlg = Find-Dialog '指定ペイン: フォルダを指定'
    (Dlg-ById $dlg 'PaneFolder1').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("$d\a")
    (Dlg-ById $dlg 'PaneFolder2').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("$d\b"); Start-Sleep -Milliseconds 400
    $before = Pane-Tab-Count
    Press (Dlg-Button $dlg 'キャンセル')
    Start-Sleep -Milliseconds 600
    Check "8) cancel creates nothing ($before -> $(Pane-Tab-Count))" ((Pane-Tab-Count) -eq $before)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
