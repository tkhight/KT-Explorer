# グループタブの画面テスト: 件数が付かないこと、ドラッグで並べ替えられること(実際のマウス)、
# 右クリックの「グループの色」で色の線が付き「色なし」で消えること(画素)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_group_tabs.ps1'
# 操作するのは %TEMP%\kt_test_ui_group_tabs の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。マウスは実際に動かす(テスト中は触らない)。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_group_tabs'

# グループタブの項目(画面の左から順)。項目の中の文字(グループ名)も返す
function Get-Groups {
    $A = $global:Kt.A
    @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*TabGroup*' } | Sort-Object { $_.Current.BoundingRectangle.X } | ForEach-Object {
        $t = $_.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
        [pscustomobject]@{ Item = $_; Name = if ($t) { $t.Current.Name } else { '' } }
    })
}
function Group-Names { (Get-Groups | ForEach-Object { $_.Name }) -join ',' }
function Add-Group([string]$name) {
    (Find-ById 'AddGroupButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 900
    Send-Text $name; Send-Keys '{ENTER}' 900
}
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    Assert-AppForeground
    [void][KtWin]::SetCursorPos($x1, $y1); Start-Sleep -Milliseconds 300
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200
    for ($i = 1; $i -le 8; $i++) { [void][KtWin]::SetCursorPos([int]($x1 + ($x2 - $x1) * $i / 8), [int]($y1 + ($y2 - $y1) * $i / 8)); [KtWin]::mouse_event(1, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80 }
    Start-Sleep -Milliseconds 250
    [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700
}
function Has-Color($item, [int]$r, [int]$g, [int]$b) {
    $rc = $item.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$rc.Width, [int]$rc.Height)
    $gr = [System.Drawing.Graphics]::FromImage($bmp); $gr.CopyFromScreen([int]$rc.X, [int]$rc.Y, 0, 0, $bmp.Size); $gr.Dispose()
    $found = $false
    for ($x = 0; $x -lt $bmp.Width; $x += 3) { for ($y = 0; $y -lt $bmp.Height; $y++) {
        $c = $bmp.GetPixel($x, $y)
        if ([Math]::Abs($c.R - $r) -le 12 -and [Math]::Abs($c.G - $g) -le 12 -and [Math]::Abs($c.B - $b) -le 12) { $found = $true }
    } }
    $bmp.Dispose(); $found
}
function Right-Click($item) {
    $rc = $item.Current.BoundingRectangle
    Assert-AppForeground
    [void][KtWin]::SetCursorPos([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2)); Start-Sleep -Milliseconds 400
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
    [void](Wait-Until { [bool](Find-AppMenuItem 'グループの色') } 4000 150)   # メニューが出きるまで待つ
    Start-Sleep -Milliseconds 300
}
function Invoke-Item([string]$name) {
    $m = Find-AppMenuItem $name
    if (-not $m) { Stop-Test "menu item '$name' not found" }
    $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
}

try {
    Start-KtApp
    Set-Foreground
    Add-Group 'GrpA'; Add-Group 'GrpB'
    [void](Wait-Until { (Get-Groups).Count -ge 3 } 3000 150)
    $names = Group-Names

    # ── 1) 件数が付かない
    Check "1) group tabs show the name only, no tab count ($names)" (($names -match 'GrpA') -and ($names -notmatch '\(\d+\)'))
    $first = (Get-Groups)[0].Name

    # ── 2) ドラッグで並べ替える: 最後(GrpB)を、先頭の位置へ
    $g = Get-Groups
    $src = $g[-1].Item.Current.BoundingRectangle; $dst = $g[0].Item.Current.BoundingRectangle
    Drag ([int]($src.X + $src.Width / 2)) ([int]($src.Y + $src.Height / 2)) ([int]($dst.X + $dst.Width / 2)) ([int]($dst.Y + $dst.Height / 2))
    [void](Wait-Until { (Get-Groups)[0].Name -eq 'GrpB' } 3000 150)
    Check "2) dragging the last group onto the first moves it to the front ($(Group-Names))" (((Get-Groups)[0].Name -eq 'GrpB') -and ((Get-Groups)[1].Name -eq $first))
    Check "2) the number of groups is unchanged" ((Get-Groups).Count -eq $g.Count)

    # ── 3) クリックだけ(動かさない)では、並び順は変わらず、そのグループが選ばれる
    $order = Group-Names
    $second = (Get-Groups)[1].Item
    Click-Element $second
    Check "3) a plain click does not reorder ($(Group-Names))" ((Group-Names) -eq $order)
    Check '3) and it selects that group' ($second.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected)

    # ── 4) 色: 右クリック → グループの色 → 赤。色なしで消える
    $target = (Get-Groups)[0].Item
    Check '4) no red line before choosing a color' (-not (Has-Color $target 0xE5 0x48 0x4D))
    Right-Click $target
    Check '4) the group menu has rename / color / delete' (([bool](Find-AppMenuItem '名前を変更')) -and ([bool](Find-AppMenuItem 'グループの色')) -and ([bool](Find-AppMenuItem 'グループを削除')))
    (Find-AppMenuItem 'グループの色').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Invoke-Item '赤'
    [void](Wait-Until { Has-Color (Get-Groups)[0].Item 0xE5 0x48 0x4D } 2500 200)
    Check '4) after choosing 赤, the group tab shows a red line' (Has-Color (Get-Groups)[0].Item 0xE5 0x48 0x4D)
    Right-Click (Get-Groups)[0].Item
    (Find-AppMenuItem 'グループの色').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Invoke-Item '色なし'
    [void](Wait-Until { -not (Has-Color (Get-Groups)[0].Item 0xE5 0x48 0x4D) } 2500 200)
    Check '4) after choosing 色なし, the red line is gone' (-not (Has-Color (Get-Groups)[0].Item 0xE5 0x48 0x4D))

    # ── 5) 一覧の背景が、表示中のグループの色を、ごく薄くしたものになる(緑 #30A46C → 243,250,246)
    Click-Element (Get-Groups)[0].Item
    Move-To $d
    Click-Element (Get-Groups)[0].Item
    $hdr = Find-All 'HeaderItem' | Where-Object { $_.Current.BoundingRectangle.Width -gt 20 } | Select-Object -First 1
    if (-not $hdr) { Stop-Test 'a column header was not found' }
    $hr = $hdr.Current.BoundingRectangle
    function List-Pixel { $bmp = New-Object System.Drawing.Bitmap(1, 1); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$hr.X + 40, [int]($hr.Y + $hr.Height) + 60, 0, 0, $bmp.Size); $g.Dispose(); $c = $bmp.GetPixel(0, 0); $bmp.Dispose(); $c }
    function Is-Rgb($c, [int]$r, [int]$g, [int]$b) { ([Math]::Abs($c.R - $r) -le 3) -and ([Math]::Abs($c.G - $g) -le 3) -and ([Math]::Abs($c.B - $b) -le 3) }
    $p = List-Pixel
    Check "5) without a group color the file list is white (got $($p.R),$($p.G),$($p.B))" (Is-Rgb $p 255 255 255)
    Right-Click (Get-Groups)[0].Item
    (Find-AppMenuItem 'グループの色').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Check '5) the color grid has the new colors (青緑, こげ茶) and "その他の色…"' (([bool](Find-AppMenuItem '青緑')) -and ([bool](Find-AppMenuItem 'こげ茶')) -and ([bool](Find-AppMenuItem 'その他の色…')))
    Invoke-Item '緑'
    [void](Wait-Until { Is-Rgb (List-Pixel) 243 250 246 } 2500 200)
    $p = List-Pixel
    Check "5) after choosing 緑, the file list is pale green (got $($p.R),$($p.G),$($p.B))" (Is-Rgb $p 243 250 246)
    $hp = $(  $bmp = New-Object System.Drawing.Bitmap(1, 1); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]($hr.X + $hr.Width) - 6, [int]$hr.Y + 4, 0, 0, $bmp.Size); $g.Dispose(); $c = $bmp.GetPixel(0, 0); $bmp.Dispose(); $c )
    Check "5) the column header row is a little darker than the list (got $($hp.R),$($hp.G),$($hp.B))" (Is-Rgb $hp 205 233 220)

    # ── 6) 「その他の色…」: 色の選択画面が開き、キャンセル(Esc)なら色は変わらない
    function Window-Count { @($global:Kt.Root.FindAll('Children', $global:Kt.PidCond)).Count }
    $before = Window-Count
    Right-Click (Get-Groups)[0].Item
    (Find-AppMenuItem 'グループの色').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Invoke-Item 'その他の色…'
    $A = $global:Kt.A
    $dialog = { $global:Kt.Win.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ClassNameProperty, '#32770'))) }
    $top = { @($global:Kt.Root.FindAll('Children', $global:Kt.PidCond)) | Where-Object { $_.Current.ClassName -eq '#32770' } }
    $opened = Wait-Until { [bool](& $dialog) -or [bool](& $top) } 4000 200
    Check '6) "その他の色…" opens the color dialog' $opened
    if ($opened) {
        Assert-AppForeground
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 800
        Check '6) cancelling the dialog closes it' (-not ([bool](& $dialog) -or [bool](& $top)))
        $p = List-Pixel
        Check "6) cancelling keeps the color (got $($p.R),$($p.G),$($p.B))" (Is-Rgb $p 243 250 246)
    }

    # ── 7) 色なし: 一覧は白に戻る
    Right-Click (Get-Groups)[0].Item
    (Find-AppMenuItem 'グループの色').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Invoke-Item '色なし'
    [void](Wait-Until { Is-Rgb (List-Pixel) 255 255 255 } 2500 200)
    $p = List-Pixel
    Check "7) after 色なし, the file list is white again (got $($p.R),$($p.G),$($p.B))" (Is-Rgb $p 255 255 255)

    # ── 8) ペイン: 参照元のグループの色と名札(2 ペインのボタンは、今のグループが参照元)
    Right-Click (Get-Groups)[0].Item
    (Find-AppMenuItem 'グループの色').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Invoke-Item '緑'
    $groupName = (Get-Groups)[0].Name
    (Find-ById 'Panes2Button').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200
    function Text-Elements([string]$name) { @($global:Kt.Win.FindAll('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($global:Kt.A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)),
        (New-Object Windows.Automation.PropertyCondition($global:Kt.A::NameProperty, $name)))))) }
    # グループタブの名前(1 個)に加えて、ペインの名札(2 個)
    [void](Wait-Until { (Text-Elements $groupName).Count -ge 3 } 3000 200)
    Check "8) each pane header has a tag with the source group name ($((Text-Elements $groupName).Count) texts '$groupName')" ((Text-Elements $groupName).Count -ge 3)
    $hdrs = @(Find-All 'HeaderItem' | Where-Object { $_.Current.BoundingRectangle.Width -gt 20 -and $_.Current.Name -like '名前*' } | Sort-Object { $_.Current.BoundingRectangle.X })
    Check '8) two panes are shown' ($hdrs.Count -eq 2)
    if ($hdrs.Count -eq 2) {
        foreach ($i in 0, 1) {
            $hr = $hdrs[$i].Current.BoundingRectangle
            $p = List-Pixel
            Check "8) pane $($i + 1): the list takes the source group color (got $($p.R),$($p.G),$($p.B))" (Is-Rgb $p 243 250 246)
        }
    }
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
