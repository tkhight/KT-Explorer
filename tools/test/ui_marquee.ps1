# 一覧の範囲選択の画面テスト: 左右の余白・名前以外の列からのドラッグで範囲選択できること、Ctrl で追加、余白のクリックで解除、
# 名前からのドラッグは、これまでどおりファイルの移動になること、下へはみ出すと自動でスクロールすること、アイコン表示でも選べること。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_marquee.ps1'
# 操作するのは %TEMP%\kt_test_ui_marquee の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。マウスは実際に動かす。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_marquee'
New-Item -ItemType Directory (Join-Path $d 'sub') | Out-Null
1..80 | ForEach-Object { Set-Content -LiteralPath (Join-Path $d ('f{0:00}.txt' -f $_)) -Value 'x' }   # フォルダと合わせて 81 項目

# 一覧の項目だけを、上から(同じ高さなら左から)。ListItem には、タブなども含まれるので、名前(FileEntry)で絞る
function Rows([string]$type = 'DataItem') {
    @(Find-All $type | Where-Object { $_.Current.Name -like '*FileEntry*' } | Sort-Object { $_.Current.BoundingRectangle.Y }, { $_.Current.BoundingRectangle.X })
}
# 一覧の要素(行の親)。選択の件数は、画面に出ていない項目も含めて、ここから数える
function List-Of($row) { [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($row) }
function Selected-Count($list) { @($list.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()).Count }
function Selected-Indexes($rows) {
    @(for ($i = 0; $i -lt $rows.Count; $i++) { if ($rows[$i].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { $i } }) -join ','
}
function Mid-Y($row) { $r = $row.Current.BoundingRectangle; [int]($r.Y + $r.Height / 2) }
function Mouse-Down { [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero) }
function Mouse-Up { [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
function Move-Mouse([int]$x, [int]$y) { [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 60 }
# 押したまま、いくつかに分けて動かして、離す
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    Assert-AppForeground
    Move-Mouse $x1 $y1; Start-Sleep -Milliseconds 600   # 直前のクリックと、ダブルクリックにならないように
    Mouse-Down; Start-Sleep -Milliseconds 100
    foreach ($i in 1..8) { Move-Mouse ([int]($x1 + ($x2 - $x1) * $i / 8)) ([int]($y1 + ($y2 - $y1) * $i / 8)) }
    Start-Sleep -Milliseconds 200
    Mouse-Up; Start-Sleep -Milliseconds 700
}
function Ctrl-Down { [KtWin]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero) }
function Ctrl-Up { [KtWin]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }

try {
    Start-KtApp
    Move-To $d
    $rows = Rows
    if ($rows.Count -lt 12) { Stop-Test "too few rows on screen ($($rows.Count))" }
    $list = List-Of $rows[0]
    $row = $rows[0].Current.BoundingRectangle
    $listLeft = $list.Current.BoundingRectangle.X
    $left = [int]($row.X - 7)                      # 左の余白
    $dateX = [int]($row.X + 280 + 30)              # 更新日時の列
    $nameX = [int]($row.X + 30)                    # 名前の文字の上

    Check "0) there is a margin on the left of the rows ($([int]($row.X - $listLeft)) px)" (($row.X - $listLeft) -ge 12)

    # ── 1) 左の余白から、行へドラッグ
    Drag $left (Mid-Y $rows[2]) ($left + 120) (Mid-Y $rows[4])
    Check "1) drag from the left margin selects the rows under the box (selected: $(Selected-Indexes $rows))" ((Selected-Indexes $rows) -eq '2,3,4')

    # ── 1b) 下の方の行でも、ずれない(行の間の余白を含めて計算していること)
    Drag $left (Mid-Y $rows[17]) ($left + 120) (Mid-Y $rows[19])
    Check "1b) rows far down are selected exactly under the box (selected: $(Selected-Indexes $rows))" ((Selected-Indexes $rows) -eq '17,18,19')

    # ── 2) 更新日時の列からドラッグ: 選び直し。ファイルは動かない
    Drag $dateX (Mid-Y $rows[6]) ($dateX + 60) (Mid-Y $rows[8])
    Check "2) drag from the date column selects with a box (selected: $(Selected-Indexes $rows))" ((Selected-Indexes $rows) -eq '6,7,8')

    # ── 3) Ctrl を押しながら: 今の選択に追加
    Ctrl-Down
    try { Drag $left (Mid-Y $rows[1]) ($left + 120) (Mid-Y $rows[2]) } finally { Ctrl-Up }
    Check "3) Ctrl + drag adds to the selection (selected: $(Selected-Indexes $rows))" ((Selected-Indexes $rows) -eq '1,2,6,7,8')

    # ── 4) 余白をクリック: 選択を解除
    Click-Point $left (Mid-Y $rows[10])
    Check "4) a click on the margin clears the selection (count: $(Selected-Count $list))" ((Selected-Count $list) -eq 0)

    # ── 5) 更新日時の列のクリックは、これまでどおり、その行の選択
    Click-Point $dateX (Mid-Y $rows[3])
    Check "5) a click on the date column selects that row (selected: $(Selected-Indexes $rows))" ((Selected-Indexes $rows) -eq '3')

    # ── 6) 右の余白からドラッグ
    $right = [int]($row.X + $row.Width + 6)
    Drag $right (Mid-Y $rows[7]) ($right - 200) (Mid-Y $rows[9])
    Check "6) drag from the right margin selects (selected: $(Selected-Indexes $rows))" ((Selected-Indexes $rows) -eq '7,8,9')

    # ── 7) 下へはみ出して止める: 自動でスクロールして、画面に無かった項目も選ばれる
    Click-Point $left (Mid-Y $rows[10])
    $bottom = [int]($list.Current.BoundingRectangle.Y + $list.Current.BoundingRectangle.Height)
    Assert-AppForeground
    Move-Mouse $left (Mid-Y $rows[1]); Start-Sleep -Milliseconds 600
    Mouse-Down; Start-Sleep -Milliseconds 100
    Move-Mouse ($left + 100) (Mid-Y $rows[5]); Move-Mouse ($left + 120) ($bottom + 20)
    [void](Wait-Until { (Selected-Count $list) -ge 80 } 12000 200)
    Mouse-Up; Start-Sleep -Milliseconds 700
    $n = Selected-Count $list
    Check "7) holding below the list scrolls and selects to the end (selected $n of 81, from the 2nd item)" ($n -eq 80)

    # ── 8) 名前からのドラッグは、これまでどおりファイルの移動(フォルダ sub へ)
    Move-To "$d\sub"; Move-To $d
    $rows = Rows
    Drag $nameX (Mid-Y $rows[3]) $nameX (Mid-Y $rows[0])
    [void](Wait-Until { Test-Path -LiteralPath (Join-Path $d 'sub\f03.txt') } 3000 150)
    Check '8) drag from a name still moves the file (f03.txt into sub)' ((Test-Path -LiteralPath (Join-Path $d 'sub\f03.txt')) -and -not (Test-Path -LiteralPath (Join-Path $d 'f03.txt')))

    # ── 9) アイコン表示: 余白から横へドラッグ
    Set-Foreground
    Send-Keys '^+2' 1800
    $items = Rows 'ListItem'
    if ($items.Count -lt 6) { $items = Rows 'DataItem' }
    if ($items.Count -lt 6) { Stop-Test "icon view items not found ($($items.Count))" }
    $a = $items[0].Current.BoundingRectangle; $c = $items[2].Current.BoundingRectangle
    $list = List-Of $items[0]
    Click-Point ([int]($a.X - 7)) (Mid-Y $items[0])
    Drag ([int]($a.X - 7)) (Mid-Y $items[0]) ([int]($c.X + $c.Width / 2)) ((Mid-Y $items[0]) + 5)
    Check "9) icon view: drag from the margin across three items selects them (count: $(Selected-Count $list))" ((Selected-Count $list) -eq 3)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
