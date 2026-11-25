# コピー・貼り付けの対象と、ツリーの Ctrl+クリックの画面テスト:
#  ① ツリーのフォルダを Ctrl+クリックすると、新しいタブで開く(いまのタブは動かない)
#  ② タブ A でコピー → タブ B をクリックして表示を切り替えた直後に Ctrl+V → タブ B に貼り付く
#  ③ ペイン A でコピー → ペイン B のヘッダー(枠)をクリックして操作中にして Ctrl+V → ペイン B に貼り付く(ペイン A ではなく)
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_paste_target.ps1'
# 操作するのは %TEMP%\kt_test_ui_paste_target の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# クリップボードを書き換える。マウスは実際に動かす(テスト中は触らない)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_paste_target'
foreach ($n in 'A', 'B', 'P', 'Q', 'T\s1', 'T\s2') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
Set-Content -LiteralPath "$d\A\x.txt" -Value 'x'
Set-Content -LiteralPath "$d\P\x2.txt" -Value 'x2'

function Get-Tabs { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' } | Sort-Object { $_.Current.BoundingRectangle.X }) }
function Get-Rows {
    $r = @(Find-All 'DataItem')
    if ($r.Count -eq 0) { $r = @(Find-All 'ListItem' | Where-Object { $_.Current.BoundingRectangle.Y -gt ($global:Kt.Win.Current.BoundingRectangle.Y + 125) }) }
    $r
}
# 名前が一致する行のうち、いちばん左のもの(ペインが並ぶときは、左のペイン)を選び、一覧にフォーカスを移す
function Select-File([string]$name) {
    $A = $global:Kt.A
    $hits = @()
    foreach ($row in Get-Rows) {
        $t = $row.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
        if ($t -and $t.Current.Name -eq $name) { $hits += $row }
    }
    if ($hits.Count -eq 0) { return $false }
    $row = $hits | Sort-Object { $_.Current.BoundingRectangle.X } | Select-Object -First 1
    $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus(); Start-Sleep -Milliseconds 300
    $true
}
function Hold-Ctrl([bool]$down) { [KtWin]::keybd_event(0x11, 0, $(if ($down) { 0 } else { 2 }), [UIntPtr]::Zero); Start-Sleep -Milliseconds 150 }

try {
    Start-KtApp

    # ── ② タブ A でコピー → タブ B を選択して表示を切り替えた直後に貼り付け
    Move-To "$d\A"; Set-Foreground; Send-Keys '^t' 300; Move-To "$d\B"
    $tabs = Get-Tabs; $tabA = $tabs[-2]; $tabB = $tabs[-1]
    Click-Element $tabA
    [void](Wait-Until { (Get-PathText) -eq "$d\A" } 3000 100)
    Check '2) tab A shows its folder' ((Get-PathText) -eq "$d\A")
    Check '2) x.txt selected in tab A' (Select-File 'x.txt')
    Send-Keys '^c' 300
    Click-Element $tabB     # キーボードフォーカスは、タブバーに残る(一覧には移らない)
    [void](Wait-Until { (Get-PathText) -eq "$d\B" } 3000 100)
    Check '2) tab B shows its folder' ((Get-PathText) -eq "$d\B")
    Send-Keys '^v' 0
    Check '2) Ctrl+V right after switching tabs pastes into tab B' (Wait-Until { Test-Path -LiteralPath "$d\B\x.txt" } 6000 150)

    # ── ③ ペイン A でコピー → ペイン B の枠(ヘッダー)をクリックして操作中にして貼り付け
    Move-To "$d\P"
    (Find-ById 'Panes2Button').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900
    Move-To "$d\Q"      # 操作中の(右の)ペインを Q へ
    Check '3) the right pane now shows Q' ((Get-PathText) -eq "$d\Q")
    Check '3) x2.txt selected in the left pane (pane A = P)' (Select-File 'x2.txt')
    Send-Keys '^c' 300
    # 右のペインのヘッダー(パスの文字)をクリックする
    $A = $global:Kt.A
    $hdr = $global:Kt.Win.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)),
        (New-Object Windows.Automation.PropertyCondition($A::NameProperty, "$d\Q")))))
    if (-not $hdr) { Stop-Test 'the header of the right pane was not found' }
    Click-Element $hdr
    Send-Keys '^v' 0
    Check '3) Ctrl+V after clicking the right pane header pastes into the right pane (Q)' (Wait-Until { Test-Path -LiteralPath "$d\Q\x2.txt" } 6000 150)
    Check '3) and not into the left pane (P still has only the original file)' (@(Get-ChildItem -LiteralPath "$d\P").Count -eq 1)

    # ── ① ツリーのフォルダを Ctrl+クリック → 新しいタブ。いまのタブは動かない
    #    いまのフォルダを s2 にして、その上に見えている兄弟の s1 を Ctrl+クリックする(ツリーは、選択中の s2 を見える位置へスクロールする)
    Move-To "$d\T\s2"
    $tabsBefore = (Get-Tabs).Count
    $A = $global:Kt.A
    $tr = (Find-All 'Tree' | Select-Object -First 1).Current.BoundingRectangle
    $script:node = $null
    Check '1) the sibling folder s1 is visible in the tree' (Wait-Until {
        $script:node = Find-All 'TreeItem' | Where-Object {
            $r = $_.Current.BoundingRectangle
            if ($r.Y -lt $tr.Y -or ($r.Y + 18) -gt ($tr.Y + $tr.Height)) { return $false }
            $t = $_.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
            $t -and $t.Current.Name -eq 's1'
        } | Select-Object -First 1
        [bool]$script:node } 4000 200)
    if ($script:node) {
        $nr = $script:node.Current.BoundingRectangle
        Set-Foreground
        Hold-Ctrl $true
        Click-Point ([int]($nr.X + 50)) ([int]($nr.Y + 9))
        Hold-Ctrl $false
        [void](Wait-Until { (Get-Tabs).Count -eq $tabsBefore + 1 } 3000 150)
        Check "1) Ctrl+click on a tree folder opens a new tab ($tabsBefore -> $((Get-Tabs).Count))" ((Get-Tabs).Count -eq $tabsBefore + 1)
        Check "1) the new tab shows that folder ($(Get-PathText))" ((Get-PathText) -eq "$d\T\s1")
        # いままでのタブ(ペインタブ。右のペインが s2)は動いていない: そのタブへ戻ると、右のペインのヘッダーが s2 のまま
        $paneTab = Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*PaneTab*' } | Select-Object -First 1
        Click-Element $paneTab
        $hasS2 = { $global:Kt.Win.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
            (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)),
            (New-Object Windows.Automation.PropertyCondition($A::NameProperty, "$d\T\s2"))))) }
        Check '1) the original tab was not moved by the Ctrl+click (its pane still shows s2)' (Wait-Until { [bool](& $hasS2) } 3000 150)
    }
}
finally {
    Hold-Ctrl $false
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
