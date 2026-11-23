# メニューバーの「ファイル」の画面テスト: 項目が並ぶこと、新規作成(フォルダー・テキスト ドキュメント)が表示中のフォルダに作られること。
# ターミナル・WinMerge は外部のアプリが起動し、「アプリケーションの終了」は、終了時にユーザーの設定ファイルを書き換えるため、ここでは押さない(項目の有無と、使える状態かだけを見る)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_file.ps1'
# 操作するのは %TEMP%\kt_test_ui_menu_file の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_menu_file'
Set-Content -LiteralPath (Join-Path $d 'a.txt') -Value 'x'

function Open-FileMenu {
    Set-Foreground
    $m = Find-AppMenuItem 'ファイル(F)'
    if (-not $m) { Stop-Test 'File menu not found' }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 800
}
function Close-Menus { Send-Keys '{ESC}' 500; Send-Keys '{ESC}' 500 }

try {
    Start-KtApp
    Move-To $d

    # ── 0) メニューバーの大項目の並び: ファイル / 編集 / 表示 / お気に入り / 履歴 / ツール / ヘルプ(左から右の順)
    $tops = @('ファイル(F)', '編集(E)', '表示(V)', 'お気に入り(A)', '履歴(R)', 'ツール(T)', 'ヘルプ(H)') | ForEach-Object { Find-AppMenuItem $_ }
    $found = @($tops | Where-Object { $_ }).Count
    $xs = @($tops | Where-Object { $_ } | ForEach-Object { $_.Current.BoundingRectangle.X })
    $ordered = $true; for ($i = 1; $i -lt $xs.Count; $i++) { if ($xs[$i] -le $xs[$i - 1]) { $ordered = $false } }
    Check "0) the menu bar has the seven top-level menus, in order (found $found)" (($found -eq 7) -and $ordered)

    # ── 1) 項目が並び、使える状態
    Open-FileMenu
    foreach ($name in 'ターミナルで開く(T)', 'WinMerge(M)', '新規作成(N)', 'プロパティ(R)', 'アプリケーションの終了(X)') {
        $item = Find-AppMenuItem $name
        Check "1) File menu has '$name' and it is enabled" ($item -and $item.Current.IsEnabled)
    }
    Close-Menus

    # ── 2) 新規作成 → フォルダー
    Open-FileMenu
    $new = Find-AppMenuItem '新規作成(N)'
    $new.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    $folder = Find-AppMenuItem 'フォルダー(F)'
    if (-not $folder) { Stop-Test 'New > Folder not found' }
    $folder.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1800
    Check '2) New > Folder creates "新しいフォルダー" in the shown folder' (Test-Path -LiteralPath (Join-Path $d '新しいフォルダー') -PathType Container)
    Send-Keys '{ESC}' 700   # 名前の入力を、既定の名前のまま閉じる

    # ── 3) 新規作成 → テキスト ドキュメント
    Open-FileMenu
    $new = Find-AppMenuItem '新規作成(N)'
    $new.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    $text = Find-AppMenuItem 'テキスト ドキュメント(T)'
    if (-not $text) { Stop-Test 'New > Text Document not found' }
    $text.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1800
    Check '3) New > Text Document creates "新しいテキスト ドキュメント.txt"' (Test-Path -LiteralPath (Join-Path $d '新しいテキスト ドキュメント.txt') -PathType Leaf)
    Send-Keys '{ESC}' 700

    # ── 4) 同じ操作をもう一度: 重複しない名前になる
    Open-FileMenu
    $new = Find-AppMenuItem '新規作成(N)'
    $new.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    (Find-AppMenuItem 'フォルダー(F)').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1800
    Check '4) a second New > Folder gets a different name (no overwrite)' (@(Get-ChildItem -LiteralPath $d -Directory).Count -eq 2)
    Send-Keys '{ESC}' 700
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
