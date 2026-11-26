# ショートカット(.lnk)を開いたとき、リンク先がフォルダなら、エクスプローラーを開かず、KT_Explorer の一覧のまま移動することの画面テスト。
# Enter、ダブルクリック、右クリックの「開く」「新しいタブで開く」を試す。ファイルへのショートカットは、フォルダとしては扱わない(試さない: 開くとアプリが起動してしまうため)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_shortcut.ps1'
# 操作するのは %TEMP%\kt_test_ui_shortcut の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_shortcut'
$links = Join-Path $d 'links'
$target = Join-Path $d 'target folder'
New-Item -ItemType Directory $links, $target | Out-Null
Set-Content -LiteralPath (Join-Path $target 'inside.txt') -Value 'x'
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut((Join-Path $links 'to_target.lnk')); $sc.TargetPath = $target; $sc.Save()
[void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc); [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($wsh)

function Select-First-Row {
    $rows = Find-All 'DataItem'
    if ($rows.Count -ne 1) { Stop-Test "unexpected row count $($rows.Count) (expected 1: the shortcut)" }
    $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[0].SetFocus(); Start-Sleep -Milliseconds 500
    $rows[0]
}
function Explorer-Windows { @((New-Object -ComObject Shell.Application).Windows() | Where-Object { $_.Name -eq 'エクスプローラー' -or $_.Name -eq 'File Explorer' }).Count }

try {
    Start-KtApp
    $before = Explorer-Windows

    # ── 1) Enter
    Move-To $links
    [void](Select-First-Row)
    Send-Keys '{ENTER}' 2000
    Check '1) Enter on a shortcut to a folder: this tab moves to the target folder' ((Get-PathText) -eq $target)

    # ── 2) ダブルクリック(実際のマウス)
    Move-To $links
    $row = Select-First-Row
    $r = $row.Current.BoundingRectangle
    [void][KtWin]::SetCursorPos([int]($r.X + 60), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 400
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 2000
    Check '2) double-click on a shortcut to a folder: this tab moves to the target folder' ((Get-PathText) -eq $target)
    Write-Output "   (after the double-click: path box='$(Get-PathText)'; row rect x=$([int]$r.X) y=$([int]$r.Y) w=$([int]$r.Width) h=$([int]$r.Height))"

    # ── 3) 右クリックメニューに「新しいタブで開く」が出て、リンク先のフォルダを新しいタブで開く
    Move-To $links
    [void](Select-First-Row)
    Open-ContextMenuByKey
    $item = Find-AppMenuItem '新しいタブで開く(E)'
    Check '3) the context menu offers "新しいタブで開く" for the shortcut' ([bool]$item)
    if ($item) {
        $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 2000
        Check '3) the new tab shows the target folder' ((Get-PathText) -eq $target)
    }

    # ── 4) エクスプローラーのウィンドウは、増えていない
    Check '4) no Windows Explorer window was opened' ((Explorer-Windows) -le $before)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
