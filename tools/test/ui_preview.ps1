# プレビューウィンドウの画面テスト(表示の切り替え、選択に応じた内容、複数選択、非表示)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_preview.ps1'
# 操作するのは %TEMP%\kt_test_ui_preview の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_preview'
Set-Content -LiteralPath (Join-Path $d 'a_note.txt') -Value 'PREVIEW_TEXT_MARKER hello' -Encoding utf8
[System.IO.File]::WriteAllBytes((Join-Path $d 'b_pixel.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='))
[System.IO.File]::WriteAllBytes((Join-Path $d 'c_data.bin'), (New-Object byte[] 64))
New-Item -ItemType Directory (Join-Path $d 'd_folder') | Out-Null

# 名前の入力欄ではない Edit(プレビューのテキスト欄)の値
function Preview-Text {
    $e = Find-All 'Edit' | Where-Object { $_.Current.AutomationId -notin 'PathBox', 'FilterBox', 'SearchBox' } | Select-Object -First 1
    Get-Text $e
}
function Has-Text([string]$text) { @(Find-All 'Text' | Where-Object { $_.Current.Name -like "*$text*" }).Count -gt 0 }
# 一覧は、フォルダーが先頭(0: d_folder、1: a_note.txt、2: b_pixel.png、3: c_data.bin)
function Select-Row([int]$index) {
    $rows = Find-All 'DataItem'
    $rows[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $rows[$index].SetFocus(); Start-Sleep -Milliseconds 1200
}
function Preview-Toggle { Find-All 'Button' | Where-Object { $_.Current.Name -eq 'プレビュー' } | Select-Object -First 1 }

try {
    Start-KtApp
    Move-To $d
    if (@(Find-All 'DataItem').Count -ne 4) { Stop-Test 'unexpected row count (expected the 4 test items)' }

    # ── 1) 既定では、プレビューは出ていない。ボタンで表示する
    $toggle = Preview-Toggle
    if (-not $toggle) { Stop-Test 'preview toggle button not found' }
    $tp = $toggle.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    Check '1) the preview is off by default' ($tp.Current.ToggleState -eq 'Off')
    Select-Row 1
    Check '1) nothing from the preview is on screen while it is off' (-not (Has-Text 'ファイルを選ぶと'))
    $tp.Toggle(); Start-Sleep -Milliseconds 1500
    Check '1) the toggle button now shows On' ($tp.Current.ToggleState -eq 'On')

    # ── 2) テキストファイルを選ぶと、内容が出る
    Select-Row 1
    Check '2) text file: the content is shown in the preview' ((Preview-Text) -like '*PREVIEW_TEXT_MARKER hello*')

    # ── 3) 画像を選ぶと、画像が出る(テキスト欄は空になる)
    Select-Row 2
    $images = @(Find-All 'Image' | Where-Object { -not $_.Current.IsOffscreen })
    Check '3) image file: an image is shown' ($images.Count -ge 1)
    Check '3) image file: the text of the previous file is gone' ((Preview-Text) -notlike '*PREVIEW_TEXT_MARKER*')
    Check '3) image file: the size is shown' (Has-Text '1 × 1 px')

    # ── 4) バイナリは、情報だけ
    Select-Row 3
    Check '4) binary file: information only (BIN ファイル)' (Has-Text 'BIN ファイル')

    # ── 5) フォルダは、中身の数
    Select-Row 0
    Check '5) folder: shows the contents count' (Has-Text '0 個のフォルダー、0 個のファイル')

    # ── 6) 複数選択
    $rows = Find-All 'DataItem'
    $rows[1].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[1].SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '+{DOWN}' 1500   # Shift+↓ で、選択を1つ広げる
    Check '6) several items selected: a message with the count' (Has-Text '2 個の項目を選択中')
    if (-not (Has-Text '2 個の項目を選択中')) {
        $sel = @(Find-All 'DataItem' | Where-Object { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }).Count
        Write-Output "   (selected rows: $sel; preview-like texts: $((@(Find-All 'Text' | Where-Object { $_.Current.Name -match '選択|ファイルを選ぶ' } | ForEach-Object { $_.Current.Name }) -join ' | '))"
    }

    # ── 7) もう一度ボタンで非表示。そのあと、Alt+P で表示できる
    $tp.Toggle(); Start-Sleep -Milliseconds 1000
    Check '7) toggled off: the preview text is gone from the screen' (-not (Has-Text '個の項目を選択中'))
    Set-Foreground
    $rows = Find-All 'DataItem'; $rows[0].SetFocus(); Start-Sleep -Milliseconds 300
    Send-Keys '%p' 1500
    Check '7) Alt+P shows the preview again' ($tp.Current.ToggleState -eq 'On')
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
