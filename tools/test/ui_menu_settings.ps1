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

    # ── 同じ名前のタブの親フォルダ名: 既定はオンで表示され、「ツール」メニューでオフ/オンできる
    function Tab-Texts([string]$text) {
        $A = $global:Kt.A
        @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' } | Where-Object {
            $_.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $text))) }).Count
    }
    New-Item -ItemType Directory "$d\PA\images", "$d\PB\images" | Out-Null
    Move-To "$d\PA\images"
    Set-Foreground; Send-Keys '^t' 500; Move-To "$d\PB\images"
    [void](Wait-Until { (Tab-Texts 'PA') -eq 1 } 3000 150)
    Check 'two tabs named "images" show their parent folders (PA, PB)' (((Tab-Texts 'PA') -eq 1) -and ((Tab-Texts 'PB') -eq 1))
    function Toggle-Hint {
        Open-Menu 'ツール(T)'
        $m = Find-AppMenuItem '同じ名前のタブに親フォルダ名を表示'
        if (-not $m) { Stop-Test 'the parent-name item was not found in the Tools menu' }
        $state = Toggle-State '同じ名前のタブに親フォルダ名を表示'
        $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
        Close-Menus
        $state
    }
    $state = Toggle-Hint
    [void](Wait-Until { (Tab-Texts 'PA') -eq 0 } 3000 150)
    Check "Tools menu: the item was on ($state), turning it off hides the parent names" (($state -eq 'On') -and ((Tab-Texts 'PA') -eq 0) -and ((Tab-Texts 'PB') -eq 0))
    $state = Toggle-Hint
    [void](Wait-Until { (Tab-Texts 'PA') -eq 1 } 3000 150)
    Check "Tools menu: turning it on again shows them ($state -> On)" (($state -eq 'Off') -and ((Tab-Texts 'PA') -eq 1))
    Move-To $d

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

    # ── ヘルプ → バージョン情報: 版番号の入った小窓が出て、閉じられる
    Open-Menu 'ヘルプ(H)'
    # 「使い方」は、ブラウザ(外部のアプリ)が開くので、押さない(項目の有無だけを見る)
    $usage = Find-AppMenuItem '使い方(H)'
    Check 'Help menu has the usage item' ($usage -and $usage.Current.IsEnabled)
    $about = Find-AppMenuItem 'バージョン情報(A)'
    if (-not $about) { Stop-Test 'About item not found' }
    $about.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $shown = Wait-Until { [bool](Find-AppMenuItem 'バージョン情報') } 4000 150
    Check 'Help > About opens the version dialog' $shown
    if ($shown) {
        $A = $global:Kt.A
        $dlg = Find-AppMenuItem 'バージョン情報'
        # 小窓の文章は、環境によって Text ではなく Pane として見えるので、種類で絞らずに名前を集める
        $texts = @($dlg.FindAll('Descendants', [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ' '
        Check "About shows the version number and the license" (($texts -match '\d+\.\d+\.\d+') -and ($texts -match 'MIT License'))
        $dlg.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
        [void](Wait-Until { -not (Find-AppMenuItem 'バージョン情報') } 3000 150)
    }
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
