# 「絞込」欄の条件指定(type: など)と、検索結果の一覧がファイル操作のあとも検索結果のままであることの画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_filter.ps1'
# 操作するのは %TEMP%\kt_test_ui_filter の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_filter'
$files = Join-Path $d 'files'
$search = Join-Path $d 'search'
New-Item -ItemType Directory $files, "$search\sub" | Out-Null
foreach ($n in 'a.png', 'b.png', 'c.txt', 'd.zip') { [System.IO.File]::WriteAllBytes((Join-Path $files $n), (New-Object byte[] 100)) }
Set-Content -LiteralPath (Join-Path $search 'hello2.txt') -Value 'x'
Set-Content -LiteralPath (Join-Path $search 'sub\hello.txt') -Value 'x'

function Row-Count { @(Find-All 'DataItem').Count }
function Has-Text([string]$text) { @(Find-All 'Text' | Where-Object { $_.Current.Name -like "*$text*" }).Count -gt 0 }

try {
    Start-KtApp

    # ── 絞込欄に条件を入力する
    Move-To $files
    Check '0) the folder shows its 4 files' ((Row-Count) -eq 4)
    Set-Foreground
    Send-Keys '^f' 500                      # Ctrl+F: 絞込欄へ
    Send-Text 'type:画像'; Start-Sleep -Milliseconds 800
    Check '1) type:画像 keeps the 2 images' ((Row-Count) -eq 2)
    Check '1) the count is shown (絞り込み: 2 / 4 件)' (Has-Text '絞り込み: 2 / 4 件')

    Send-Keys '^a' 200; Send-Text '-type:画像'; Start-Sleep -Milliseconds 800
    Check '2) -type:画像 keeps the other 2' ((Row-Count) -eq 2)

    Send-Keys '^a' 200; Send-Text '*.png size:>=100'; Start-Sleep -Milliseconds 800
    Check '3) two conditions are combined (*.png and size)' ((Row-Count) -eq 2)

    Send-Keys '^a' 200; Send-Text 'size:abc'; Start-Sleep -Milliseconds 800
    Check '4) a condition that cannot be read is ignored: all 4 remain' ((Row-Count) -eq 4)
    Check '4) and the reason is shown' (Has-Text '条件を解釈できません: size:abc')

    Send-Keys '{ESC}' 800
    Check '5) Esc clears the filter: all 4, no count' (((Row-Count) -eq 4) -and -not (Has-Text '絞り込み:'))

    # ── 検索結果の一覧で名前を変えても、検索結果のままである
    Move-To $search
    $box = Find-ById 'SearchBox'
    $box.SetFocus(); Start-Sleep -Milliseconds 300
    Send-Text 'hello'; Send-Keys '{ENTER}' 2500
    Check '6) the search shows both files (2 rows)' ((Row-Count) -eq 2)
    Check '6) the file in the subfolder is listed by its relative path' (Has-Text 'sub\hello.txt')

    $row = Find-All 'DataItem' | Where-Object {
        $t = $_.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($global:Kt.A::NameProperty, 'hello2.txt')))
        [bool]$t } | Select-Object -First 1
    if (-not $row) { Stop-Test 'the row of hello2.txt was not found' }
    $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus(); Start-Sleep -Milliseconds 400
    Send-Keys '{F2}' 1200
    $e = Get-InlineEdit
    if (-not $e -or -not $e.Current.HasKeyboardFocus -or (Get-Text $e) -ne 'hello2.txt') { Stop-Test "F2 edit not as expected (text='$(Get-Text $e)')" }
    Send-Text 'renamed2'; Send-Keys '{ENTER}' 2500   # 名前の入力は、拡張子を除いて選択された状態で始まる(拡張子 .txt はそのまま)
    Check '7) the file was renamed on disk' ((Test-Path -LiteralPath (Join-Path $search 'renamed2.txt')) -and -not (Test-Path -LiteralPath (Join-Path $search 'hello2.txt')))
    Check '7) the list is still the search result (the subfolder file is shown by its relative path)' (Has-Text 'sub\hello.txt')
    # 新しい名前(renamed2.txt)は、検索語(hello)に合わないので、再検索した結果から外れる(結果は、いまの状態を表す)
    Check '7) the renamed file no longer matches the query, so the result now has 1 row' (((Row-Count) -eq 1) -and -not (Has-Text 'renamed2.txt'))
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
