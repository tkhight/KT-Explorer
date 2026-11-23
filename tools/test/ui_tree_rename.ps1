# ツリーの「新規作成」「名前の変更」(その場で入力)の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_tree_rename.ps1'
# 操作するのは %TEMP%\kt_test_tree の中だけ。KT_Explorer が起動中なら中止する。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_tree'
New-Item -ItemType Directory (Join-Path $d 'aaa') | Out-Null
try {
    Start-KtApp
    Move-To $d

    # ── 安全確認: 選択中のツリー項目が、テスト用フォルダであること(F2 の入力欄の中身で確かめる)
    $sel = Get-SelectedTreeItem
    if (-not $sel) { Stop-Test 'no selected tree item' }
    $sel.SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or (Get-Text $e) -ne 'kt_test_tree') { Stop-Test "selected tree item is not the test folder (edit shows '$(Get-Text $e)')" }
    Send-Keys '{ESC}' 800

    # ── 1) 新規作成 → その場で入力
    Open-ContextMenuByKey
    $m = Find-AppMenuItem '新規作成'
    if (-not $m) { Stop-Test 'context menu did not open' }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 500
    $f = Find-AppMenuItem 'フォルダー'
    $f.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 2500
    $e = Get-InlineEdit
    Check '1) new folder: inline edit shown with focus' ($e -and $e.Current.HasKeyboardFocus)
    Check '1) new folder: default name in the box' ((Get-Text $e) -eq '新しいフォルダー')
    Check '1) new folder: created on disk' (Test-Path -LiteralPath (Join-Path $d '新しいフォルダー'))
    if ($e -and $e.Current.HasKeyboardFocus -and (Get-Text $e) -eq '新しいフォルダー') {
        Send-Text 'Newdir'; Send-Keys '{ENTER}' 1500
        Check '1) typed name applied' (Test-Path -LiteralPath (Join-Path $d 'Newdir'))
        Check '1) default name gone' (-not (Test-Path -LiteralPath (Join-Path $d '新しいフォルダー')))
        Check '1) edit box closed' (-not (Get-InlineEdit))
    }

    # ── 2) F2 で名前の変更。表示中のタブが追従する
    Move-To (Join-Path $d 'Newdir')
    $sel = Get-SelectedTreeItem; $sel.SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'Newdir') { Stop-Test "F2 edit not as expected (shown=$([bool]$e) text='$(Get-Text $e)')" }
    Check '2) F2: inline edit with focus and current name' $true
    Send-Text 'Renamed'; Send-Keys '{ENTER}' 1800
    Check '2) renamed on disk' ((Test-Path -LiteralPath (Join-Path $d 'Renamed')) -and -not (Test-Path -LiteralPath (Join-Path $d 'Newdir')))
    Check '2) the tab showing the folder follows (path box)' ((Get-PathText) -eq (Join-Path $d 'Renamed'))
    Write-Output "   (path box now: '$(Get-PathText)')"

    # ── 3) Esc で取り消し
    $sel = Get-SelectedTreeItem; $sel.SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'Renamed') { Stop-Test "F2 edit not as expected (text='$(Get-Text $e)')" }
    Send-Text 'XYZ'; Send-Keys '{ESC}' 1000
    Check '3) Esc cancels: name unchanged' ((Test-Path -LiteralPath (Join-Path $d 'Renamed')) -and -not (Test-Path -LiteralPath (Join-Path $d 'XYZ')))
    Check '3) Esc cancels: edit box closed' (-not (Get-InlineEdit))

    # ── 4) 使えない文字は入力できない
    $sel = Get-SelectedTreeItem; $sel.SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'Renamed') { Stop-Test "F2 edit not as expected (text='$(Get-Text $e)')" }
    Send-Text 'a:b'; Start-Sleep -Milliseconds 300
    Check "4) ':' is rejected (box shows 'ab')" ((Get-Text (Get-InlineEdit)) -eq 'ab')

    # ── 5) 入力欄の中のクリックでは確定しない
    Click-Element (Get-InlineEdit)
    Check '5) click inside the box keeps editing' ([bool](Get-InlineEdit))
    Send-Keys '{ESC}' 800

    # ── 6) フォーカスを受け取れない場所(ラベル)をクリックすると確定する
    $sel = Get-SelectedTreeItem; $sel.SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'Renamed') { Stop-Test "F2 edit not as expected (text='$(Get-Text $e)')" }
    Send-Text 'Moved'
    $A = $global:Kt.A
    $label = $global:Kt.Win.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)),
        (New-Object Windows.Automation.PropertyCondition($A::NameProperty, '絞込:')))))
    if (-not $label) { Stop-Test 'label not found' }
    Click-Element $label
    Check '6) click on a non-focusable label commits' ((Test-Path -LiteralPath (Join-Path $d 'Moved')) -and -not (Test-Path -LiteralPath (Join-Path $d 'Renamed')))
    Check '6) edit box closed' (-not (Get-InlineEdit))
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
