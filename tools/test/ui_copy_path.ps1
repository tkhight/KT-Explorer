# パスのコピーの画面テスト: パンくずの右クリック(パスをコピー)、パス欄の右クリック(パス全体をコピー)、タブの右クリック(パスをコピー)。
# 長いパスのフォルダで確認する(パス欄が狭くて全体を選べなくても、右クリックでコピーできること)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_copy_path.ps1'
# 操作するのは %TEMP%\kt_test_ui_copy_path の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# クリップボードを書き換える。マウスは実際に動かす(テスト中は触らない)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_copy_path'
$long = "$d\long_folder_name_number_one_for_the_test\long_folder_name_number_two_for_the_test\long_folder_name_number_three_for_the_test"
New-Item -ItemType Directory $long | Out-Null

function Right-ClickAt([int]$x, [int]$y) {
    Assert-AppForeground
    [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200
}
function Invoke-Item([string]$name) {
    $m = Find-AppMenuItem $name
    if (-not $m) { Stop-Test "menu item '$name' not found" }
    $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
}
function Test-Selected($el) { try { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false } }
function Get-SelectedTab { Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' } | Where-Object { Test-Selected $_ } | Select-Object -First 1 }
function Clip { try { (Get-Clipboard -Raw).Trim() } catch { '' } }

try {
    Start-KtApp
    Move-To $long
    $pb = (Find-ById 'PathBox').Current.BoundingRectangle
    Check "the path is longer than the path box can show (path $($long.Length) chars)" ($long.Length -gt 100)

    # ── 1) パンくず(入力していないとき): 右クリック → 「パスをコピー」
    # Enter で移動した直後は、パス欄にフォーカスが残り、パンくずが隠れている。タブをクリックして、フォーカスを外す
    Click-Element (Get-SelectedTab)
    Set-Clipboard -Value 'x'
    Set-Foreground
    Right-ClickAt ([int]($pb.X + $pb.Width - 6)) ([int]($pb.Y + $pb.Height / 2))
    Check '1) the breadcrumb menu has "パスをコピー"' ([bool](Find-AppMenuItem 'パスをコピー'))
    Check '1) the breadcrumb menu has "パスを入力する"' ([bool](Find-AppMenuItem 'パスを入力する'))
    Invoke-Item 'パスをコピー'
    Check "1) the full path of the current folder is on the clipboard (got: '$(Clip)')" ((Clip) -eq $long)

    # ── 2) パス欄(入力中): 右クリック → 「パス全体をコピー」
    Set-Clipboard -Value 'x'
    Set-Foreground
    Send-Keys '^l' 800   # パスの入力に切り替える(パンくずが隠れる)
    Right-ClickAt ([int]($pb.X + $pb.Width / 2)) ([int]($pb.Y + $pb.Height / 2))
    Check '2) the path box menu has "パス全体をコピー"' ([bool](Find-AppMenuItem 'パス全体をコピー'))
    Invoke-Item 'パス全体をコピー'
    Check '2) the whole path is on the clipboard (nothing needs to be selected)' ((Clip) -eq $long)
    Send-Keys '{ESC}' 300
    # 入力を終える(フォルダの一覧へ)

    # ── 3) タブの右クリック → 「パスをコピー」
    Set-Clipboard -Value 'x'
    $tab = Get-SelectedTab
    $rc = $tab.Current.BoundingRectangle
    Set-Foreground
    Right-ClickAt ([int]($rc.X + $rc.Width / 2)) ([int]($rc.Y + $rc.Height / 2))
    Invoke-Item 'パスをコピー'
    Check '3) the tab menu copies the path of that tab' ((Clip) -eq $long)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
