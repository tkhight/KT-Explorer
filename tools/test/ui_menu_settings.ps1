# 旧「設定」ボタンの項目を、「表示」「ツール」メニューへ振り分けた画面テスト: 項目の有無、オン/オフの切り替え、「設定」ボタンが無いこと。
# 「Windows 起動時に自動起動」は、レジストリを書き換えるため、押さない(項目の有無だけを見る)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_settings.ps1'
# 変更はアプリの中だけ(終了時の保存は走らない)。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_menu_settings'
Set-Content -LiteralPath (Join-Path $d 'a.txt') -Value 'x'

function Open-Menu([string]$name) {
    Set-Foreground
    $m = Find-AppMenuItem $name
    if (-not $m) { Stop-Test "menu '$name' not found" }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 900
}
function Close-Menus { Send-Keys '{ESC}' 500; Send-Keys '{ESC}' 500 }
function Toggle-State([string]$name) {
    $m = Find-AppMenuItem $name
    if (-not $m) { return $null }
    "$($m.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState)"
}

try {
    Start-KtApp
    Move-To $d

    Check 'the old "設定 ▾" button is gone' (-not (@(Find-All 'Button') | Where-Object { $_.Current.Name -like '設定*' }))

    # ── 表示メニュー: ツリー連動 / プレビュー / フォルダーを先頭に
    Open-Menu '表示(V)'
    foreach ($n in 'ツリーをタブに連動', 'プレビューを表示', 'フォルダーを先頭にまとめる') {
        Check "View menu has '$n'" ([bool](Find-AppMenuItem $n))
    }
    Check 'View menu no longer has the mouse-gesture item' (-not (Find-AppMenuItem 'マウスジェスチャ(右ドラッグ)'))
    # 切り替え: フォルダーを先頭にまとめる(既定はオン → オフ)
    $before = Toggle-State 'フォルダーを先頭にまとめる'
    (Find-AppMenuItem 'フォルダーを先頭にまとめる').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    $after = Toggle-State 'フォルダーを先頭にまとめる'
    Check "View menu: 'folders first' toggles ($before -> $after)" (($before -ne $after) -and $before -and $after)
    Close-Menus

    # ── ツール メニュー
    Open-Menu 'ツール(T)'
    foreach ($n in 'マウスジェスチャ(右ドラッグ)', '開いた Explorer を取り込む', '閉じるボタンでトレイに常駐', 'フォルダーのサイズを自動計算', 'Windows 起動時に自動起動') {
        Check "Tools menu has '$n'" ([bool](Find-AppMenuItem $n))
    }
    $before = Toggle-State 'フォルダーのサイズを自動計算'
    (Find-AppMenuItem 'フォルダーのサイズを自動計算').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    $after = Toggle-State 'フォルダーのサイズを自動計算'
    Check "Tools menu: 'auto folder size' toggles ($before -> $after)" (($before -ne $after) -and $before -and $after)
    Close-Menus
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
