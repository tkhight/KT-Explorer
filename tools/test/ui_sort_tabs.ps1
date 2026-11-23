# 列見出しでの並べ替えが、タブごとに独立していること(見出しの ▲▼ がタブに合わせて変わること)の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_sort_tabs.ps1'
# 表示するフォルダは %TEMP%\kt_test_sort の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_sort'
foreach ($n in 'a', 'b') { Set-Content -LiteralPath (Join-Path $d "$n.txt") -Value 'x' }

function Header-Names { @(Find-All 'HeaderItem' | ForEach-Object { $_.Current.Name }) }
function Click-Header([string]$name) {
    $h = Find-All 'HeaderItem' | Where-Object { $_.Current.Name -like "$name*" } | Select-Object -First 1
    if (-not $h) { Stop-Test "header '$name' not found" }
    $h.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
}

try {
    Start-KtApp
    Move-To $d
    if (@(Find-All 'DataItem').Count -ne 2) { Stop-Test 'unexpected row count (expected the 2 test files)' }

    # ── タブ1: サイズで並べる
    Click-Header 'サイズ'
    $h = Header-Names
    Check '1) tab 1: clicking "サイズ" shows ▲ on it' (($h -contains 'サイズ ▲') -and -not ($h -contains '名前 ▲'))

    # ── タブ2: 新しいタブは、最後に使った並び順(サイズ ▲)で始まる。ここで名前に変える
    Set-Foreground
    Send-Keys '^t' 1200
    $h = Header-Names
    Check '2) new tab starts with the last used sort (サイズ ▲)' ($h -contains 'サイズ ▲')
    Click-Header '名前'
    $h = Header-Names
    Check '2) tab 2: clicking "名前" moves ▲ to it' (($h -contains '名前 ▲') -and -not ($h -contains 'サイズ ▲'))

    # ── タブ1へ戻る: タブ1の並び順(サイズ ▲)のまま
    Set-Foreground
    Send-Keys '^+{TAB}' 1200
    $h = Header-Names
    Check '3) back on tab 1: its own sort (サイズ ▲) is kept, not tab 2''s' (($h -contains 'サイズ ▲') -and -not ($h -contains '名前 ▲'))

    # ── タブ2へ進む: タブ2の並び順(名前 ▲)
    Set-Foreground
    Send-Keys '^{TAB}' 1200
    $h = Header-Names
    Check '4) tab 2 again: its own sort (名前 ▲)' (($h -contains '名前 ▲') -and -not ($h -contains 'サイズ ▲'))
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
