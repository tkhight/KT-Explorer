# フォルダのサイズ計算(右クリックでの手動計算 / 設定での自動計算)の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_folder_size.ps1'
# 操作するのは %TEMP%\kt_test_size の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_size'
$manual = Join-Path $d 'manual'   # 手動計算を試すフォルダ(中に sz = 2048 バイト)
$auto   = Join-Path $d 'auto'     # 自動計算を試すフォルダ(中に sz2 = 4096 バイト)
foreach ($p in "$manual\sz", "$auto\sz2") { New-Item -ItemType Directory $p | Out-Null }
[System.IO.File]::WriteAllBytes("$manual\sz\data.bin", (New-Object byte[] 2048))
[System.IO.File]::WriteAllBytes("$auto\sz2\data.bin", (New-Object byte[] 4096))

function Has-Text([string]$text) { @(Find-All 'Text' | Where-Object { $_.Current.Name -eq $text }).Count -gt 0 }

try {
    Start-KtApp

    # ── 1) 手動: フォルダを選んで右クリック → 「フォルダーのサイズを計算」
    Move-To $manual
    $rows = Find-All 'DataItem'
    if ($rows.Count -ne 1) { Stop-Test "unexpected row count $($rows.Count) (expected 1: the test folder)" }
    Check '1) size is empty before calculating' (-not (Has-Text '2 KB'))
    $rows[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[0].SetFocus(); Start-Sleep -Milliseconds 400
    Open-ContextMenuByKey
    $m = Find-AppMenuItem 'フォルダーのサイズを計算'
    if (-not $m) { Stop-Test 'menu item "フォルダーのサイズを計算" not found' }
    $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 2500
    Check '1) manual calculation shows the size (2 KB)' (Has-Text '2 KB')

    # ── 1b) 別のフォルダへ移って戻っても、計算済みのサイズは、計算し直さずに(自動計算がオフのまま)すぐ表示される
    Move-To $d
    Check '1b) in the parent folder the cached size is not shown for other folders' (-not (Has-Text '2 KB'))
    Move-To $manual
    Start-Sleep -Milliseconds 800
    Check '1b) back in the folder: the size calculated earlier is shown again at once (from the cache, auto calculation is off)' (Has-Text '2 KB')

    # ── 2) 自動: 設定で「フォルダーのサイズを自動計算」をオン → 別のフォルダへ移動すると、操作なしで計算される
    Set-Foreground
    $settings = Find-All 'Button' | Where-Object { $_.Current.Name -like '設定*' } | Select-Object -First 1
    if (-not $settings) { Stop-Test 'settings button not found' }
    $settings.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    $item = Find-AppMenuItem 'フォルダーのサイズを自動計算'
    if (-not $item) { Stop-Test 'settings item not found' }
    Check '2) auto calculation is off by default' ($item.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'Off')
    $item.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 500
    Send-Keys '{ESC}' 600
    Move-To $auto
    Start-Sleep -Milliseconds 2500
    Check '2) auto calculation shows the size without any request (4 KB)' (Has-Text '4 KB')
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
