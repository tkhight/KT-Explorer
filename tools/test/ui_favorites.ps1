# お気に入りの整理ウィンドウ(並べ替え・グループの追加と移動)と、メニューへの反映の画面テスト。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_favorites.ps1'
# 操作するフォルダは %TEMP%\kt_test_ui_fav の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 注意: 変更はアプリの中だけ(終了時の保存は走らない)ので、ユーザーのお気に入りは変わらない。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_fav'
New-Item -ItemType Directory (Join-Path $d 'testFavA'), (Join-Path $d 'testFavB') | Out-Null

$OrganizeName = 'お気に入りを整理…(並べ替え・グループ・区切り線・編集・削除)'

function Open-FavoritesMenu {
    Set-Foreground
    $m = Find-AppMenuItem 'お気に入り(A)'
    if (-not $m) { Stop-Test 'Favorites menu not found' }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 900
}
function Menu-Y([string]$name) { $m = Find-AppMenuItem $name; if ($m) { $m.Current.BoundingRectangle.Y } else { $null } }
function Find-Dialog([string]$title) {
    $A = $global:Kt.A
    for ($i = 0; $i -lt 20; $i++) {
        $byName = New-Object Windows.Automation.PropertyCondition($A::NameProperty, $title)
        $w = $global:Kt.Root.FindFirst('Children', (New-Object Windows.Automation.AndCondition($global:Kt.PidCond, $byName)))
        if (-not $w) { $w = $global:Kt.Win.FindFirst('Descendants', $byName) }   # 持ち主のウィンドウの下に出ることもある
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    }
}
function Dlg-Button($dlg, [string]$name) {
    $A = $global:Kt.A
    $dlg.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Button)),
        (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)))))
}
function Press($button) { $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900 }
function Tree-Text($dlg, [string]$prefix) {
    $A = $global:Kt.A
    $dlg.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text))) |
        Where-Object { $_.Current.Name.StartsWith($prefix) } | Select-Object -First 1   # -like だと、[ ] がワイルドカードの文字クラスになってしまう
}

try {
    Start-KtApp

    # ── 準備: 2つのフォルダをお気に入りに追加する
    foreach ($n in 'testFavA', 'testFavB') {
        Move-To (Join-Path $d $n)
        Open-FavoritesMenu
        $add = Find-AppMenuItem 'お気に入りに追加(現在のフォルダ)'
        if (-not $add) { Stop-Test 'add menu item not found' }
        $add.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    }
    Open-FavoritesMenu
    $ya = Menu-Y 'testFavA'; $yb = Menu-Y 'testFavB'
    Check '1) both favorites are in the menu, A above B (the order they were added)' (($null -ne $ya) -and ($null -ne $yb) -and ($ya -lt $yb))
    $org = Find-AppMenuItem $OrganizeName
    if (-not $org) { Stop-Test 'organize menu item not found' }
    $org.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()

    # ── 2) 整理ウィンドウで、B を選んで「上へ」→ OK
    $dlg = Find-Dialog 'お気に入りの整理'
    Check '2) the organize window opens' ([bool]$dlg)
    if (-not $dlg) { Stop-Test 'organize window did not open' }
    Start-Sleep -Milliseconds 800
    $tb = Tree-Text $dlg 'testFavB'
    if (-not $tb) { Stop-Test 'testFavB row not found in the organize window' }
    Click-Element $tb
    $up = Dlg-Button $dlg '上へ'
    Check '2) "上へ" is enabled for B' ($up.Current.IsEnabled)
    Press $up
    Press (Dlg-Button $dlg 'OK')
    Open-FavoritesMenu
    $ya = Menu-Y 'testFavA'; $yb = Menu-Y 'testFavB'
    Check '2) after OK the menu shows B above A' (($null -ne $ya) -and ($null -ne $yb) -and ($yb -lt $ya))
    Send-Keys '{ESC}' 600

    # ── 3) グループを追加して、B をその中へ移す
    Open-FavoritesMenu
    (Find-AppMenuItem $OrganizeName).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $dlg = Find-Dialog 'お気に入りの整理'
    if (-not $dlg) { Stop-Test 'organize window did not open (2nd time)' }
    Start-Sleep -Milliseconds 800
    Press (Dlg-Button $dlg 'グループを追加…')
    $ask = Find-Dialog 'グループを追加'
    Check '3) the group name dialog opens' ([bool]$ask)
    if (-not $ask) { Stop-Test 'group name dialog did not open' }
    $A = $global:Kt.A
    $edit = $ask.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
    $edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('TestGrp')
    Press (Dlg-Button $ask 'OK')
    Check '3) the new group is in the list' ([bool](Tree-Text $dlg '[グループ] TestGrp'))

    $tb = Tree-Text $dlg 'testFavB'
    Click-Element $tb
    Press (Dlg-Button $dlg 'グループへ移動 ▾')
    $into = Find-AppMenuItem 'グループ「TestGrp」の中へ'
    Check '3) the move menu lists the group' ([bool]$into)
    if (-not $into) { Stop-Test 'move menu did not list the group' }
    $into.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800
    Press (Dlg-Button $dlg 'OK')

    # ── 4) メニューでは、グループがサブメニューになり、B はその中にある
    Open-FavoritesMenu
    $grp = Find-AppMenuItem 'TestGrp'
    Check '4) the menu has the group as a submenu' ([bool]$grp)
    if ($grp) {
        $grp.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
        $inner = Find-AppMenuItem 'testFavB'
        Check '4) testFavB is inside the group submenu (to the right of the group item)' ([bool]$inner -and ($inner.Current.BoundingRectangle.X -gt $grp.Current.BoundingRectangle.X + 20))
    }
    Check '4) testFavA is still at the top level' ([bool](Find-AppMenuItem 'testFavA'))
    Send-Keys '{ESC}' 400; Send-Keys '{ESC}' 400
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
