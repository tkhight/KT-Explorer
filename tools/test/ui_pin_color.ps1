# ピン留めとタブの色の画面テスト: タブの右クリックから「タブをピン留め」→ 別のフォルダへ移ると新しいタブで開く → 「ピン留めを外す」、
# 「タブの色」から色を選ぶと、そのタブの下に色の線が出る(画面の色を調べる)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_pin_color.ps1'
# 操作するのは %TEMP%\kt_test_ui_pin_color の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。マウスは実際に動かす(テスト中は触らない)。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_pin_color'
foreach ($n in 'home\sub', 'other') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }

function Get-Tabs { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' } | Sort-Object { $_.Current.BoundingRectangle.X }) }
function Get-SelectedTab { Get-Tabs | Where-Object { try { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false } } | Select-Object -First 1 }
function Open-TabMenu($tab) {
    $rc = $tab.Current.BoundingRectangle
    Assert-AppForeground
    [void][KtWin]::SetCursorPos([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2)); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200
}
function Invoke-Item([string]$name) {
    $m = Find-AppMenuItem $name
    if (-not $m) { Stop-Test "menu item '$name' not found" }
    $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
}
# タブの領域の中を縦に調べて、指定の色(許容の範囲つき)に近い画素があるか
function Has-Color($tab, [int]$r, [int]$g, [int]$b) {
    $rc = $tab.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$rc.Width, [int]$rc.Height)
    $gr = [System.Drawing.Graphics]::FromImage($bmp)
    $gr.CopyFromScreen([int]$rc.X, [int]$rc.Y, 0, 0, $bmp.Size)
    $found = $false
    foreach ($x in [int]($rc.Width * 0.3), [int]($rc.Width * 0.5), [int]($rc.Width * 0.7)) {
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            $c = $bmp.GetPixel($x, $y)
            if ([Math]::Abs($c.R - $r) -le 12 -and [Math]::Abs($c.G - $g) -le 12 -and [Math]::Abs($c.B - $b) -le 12) { $found = $true }
        }
    }
    $gr.Dispose(); $bmp.Dispose()
    $found
}

try {
    Start-KtApp
    Move-To "$d\home"
    $t0 = (Get-Tabs).Count

    # ── 1) ピン留め前: パス欄からの移動は、そのタブの中で移動する
    Move-To "$d\home\sub"
    Check "1) not pinned: navigating keeps the tab count ($t0 -> $((Get-Tabs).Count))" ((Get-Tabs).Count -eq $t0)
    Move-To "$d\home"

    # ── 2) 選択中のタブを右クリック → 「タブをピン留め」
    $tab = Get-SelectedTab
    Open-TabMenu $tab
    Check '2) the tab menu has "タブをピン留め"' ([bool](Find-AppMenuItem 'タブをピン留め'))
    Check '2) the tab menu has "タブの色"' ([bool](Find-AppMenuItem 'タブの色'))
    Invoke-Item 'タブをピン留め'
    Open-TabMenu (Get-SelectedTab)
    Check '2) after pinning, the menu offers "ピン留めを外す"' ([bool](Find-AppMenuItem 'ピン留めを外す'))
    $closeItem = Find-AppMenuItem 'タブを閉じる'
    Check '2) a pinned tab cannot be closed: "タブを閉じる" is disabled' (($null -ne $closeItem) -and (-not $closeItem.Current.IsEnabled))
    Send-Keys '{ESC}' 600

    # ── 3) ピン留めしたタブから別のフォルダへ → 新しいタブができて、それが前面に出る
    $before = (Get-Tabs).Count
    Move-To "$d\home\sub"
    Check "3) moving away from the pinned folder opens a new tab ($before -> $((Get-Tabs).Count))" ((Get-Tabs).Count -eq $before + 1)
    Check '3) the new tab shows the destination' ((Get-PathText) -eq "$d\home\sub")

    # ── 4) ピン留めしたタブへ戻ると、そのタブは元のフォルダのまま
    $pinned = (Get-Tabs | Where-Object { $_.Current.Name -like '*FolderTab*' })[$t0 - 1]
    Click-Element $pinned
    Check '4) the pinned tab still shows its folder' ((Get-PathText) -eq "$d\home")

    # ── 5) ピン留めを外すと、また、そのタブの中で移動する
    Open-TabMenu (Get-SelectedTab)
    Invoke-Item 'ピン留めを外す'
    $before = (Get-Tabs).Count
    Move-To "$d\other"
    Check "5) after unpinning, navigating keeps the tab count ($before -> $((Get-Tabs).Count))" ((Get-Tabs).Count -eq $before)

    # ── 6) 色: 「タブの色」→「赤」で、そのタブに赤い線が出る。「色なし」で消える
    $sel = Get-SelectedTab
    Check '6) no red line before choosing a color' (-not (Has-Color $sel 0xE5 0x48 0x4D))
    Open-TabMenu $sel
    $cm = Find-AppMenuItem 'タブの色'
    $cm.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 800
    Check '6) the color submenu has the palette colors' (([bool](Find-AppMenuItem '赤')) -and ([bool](Find-AppMenuItem '緑')) -and ([bool](Find-AppMenuItem '色なし')))
    Invoke-Item '赤'
    Start-Sleep -Milliseconds 500
    Check '6) after choosing 赤, the tab shows a red line' (Has-Color (Get-SelectedTab) 0xE5 0x48 0x4D)
    Open-TabMenu (Get-SelectedTab)
    $cm = Find-AppMenuItem 'タブの色'
    $cm.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 800
    Invoke-Item '色なし'
    Start-Sleep -Milliseconds 500
    Check '6) after choosing 色なし, the red line is gone' (-not (Has-Color (Get-SelectedTab) 0xE5 0x48 0x4D))
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
