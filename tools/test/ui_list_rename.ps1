# 一覧(フォルダビュー)の名前の変更(その場で入力)の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_list_rename.ps1'
# 操作するのは %TEMP%\kt_test_list の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_list'
Set-Content -LiteralPath (Join-Path $d 'aaa.txt') -Value 'x'
try {
    Start-KtApp
    Move-To $d

    $rows = Find-All 'DataItem'
    if ($rows.Count -ne 1) { Stop-Test "unexpected row count $($rows.Count) (expected 1: the test folder)" }
    $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $rows[0].SetFocus(); Start-Sleep -Milliseconds 400

    # ── 1) F2 → 入力欄の中身が現在の名前
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'aaa.txt') { Stop-Test "F2 edit not as expected (shown=$([bool]$e) text='$(Get-Text $e)')" }
    Check '1) F2: inline edit with focus and current name' $true

    # ── 2) 入力欄の中のクリックでは確定しない / Esc で取り消し
    Click-Element $e
    Check '2) click inside the box keeps editing' ([bool](Get-InlineEdit))
    Send-Keys '{ESC}' 800
    Check '2) Esc: edit closed, file unchanged' ((-not (Get-InlineEdit)) -and (Test-Path -LiteralPath (Join-Path $d 'aaa.txt')))

    # ── 3) 一覧の空白部分をクリックすると確定する
    $rows = Find-All 'DataItem'
    $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[0].SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'aaa.txt') { Stop-Test "F2 edit not as expected (text='$(Get-Text $e)')" }
    Send-Text 'bbb'
    $r = $rows[0].Current.BoundingRectangle; $wr = $global:Kt.Win.Current.BoundingRectangle
    $x = [int]($r.X + 120); $y = [int]($r.Y + $r.Height + 120)
    if ($y -gt ($wr.Y + $wr.Height - 80)) { Stop-Test 'blank point is outside the window' }
    Click-Point $x $y
    Check '3) click on the blank area commits' ((Test-Path -LiteralPath (Join-Path $d 'bbb.txt')) -and -not (Test-Path -LiteralPath (Join-Path $d 'aaa.txt')))
    Check '3) edit box closed' (-not (Get-InlineEdit))
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
