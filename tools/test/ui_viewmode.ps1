# 表示の切り替え(詳細・アイコン・一覧)の画面テスト: メニューとショートカットでの切り替え、列見出しの有無、
# アイコン・一覧での矢印キーの移動(見た目どおりの上下左右)、名前の変更、メニューからの並べ替え、詳細へ戻ること。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_viewmode.ps1'
# 操作するのは %TEMP%\kt_test_ui_viewmode の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_viewmode'
1..80 | ForEach-Object { Set-Content -LiteralPath (Join-Path $d ('f{0:00}.txt' -f $_)) -Value 'x' }
$pixels = New-Object byte[] (64 * 64 * 4); for ($i = 0; $i -lt $pixels.Length; $i += 4) { $pixels[$i] = 200; $pixels[$i + 1] = 80; $pixels[$i + 2] = 30; $pixels[$i + 3] = 255 }
Add-Type -AssemblyName PresentationCore
$bmp = [System.Windows.Media.Imaging.BitmapSource]::Create(64, 64, 96, 96, [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, 256)
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
$fs = [System.IO.File]::Create((Join-Path $d 'picture.png')); $enc.Save($fs); $fs.Dispose()   # 全部で 81 項目

# 画面要素(UI Automation)には、見えている範囲の項目だけが出る(全部で 81 項目あっても、数十件)。そのため、件数は「十分にある」で確かめる
# 一覧の行。表示を切り替えると、画面要素の種類が DataItem / ListItem のどちらにもなる(同じ行の部品を使い回すため)ので、両方を見る
function Get-Rows {
    $r = @(Find-All 'DataItem')
    if ($r.Count -eq 0) {
        # ListItem には、グループの見出し(デフォルト (2))も含まれるので、一覧の領域(タブの下)にあるものだけを取る
        $top = $global:Kt.Win.Current.BoundingRectangle.Y + 125
        $r = @(Find-All 'ListItem' | Where-Object { $_.Current.BoundingRectangle.Y -gt $top })
    }
    $r
}
function Row-Count { @(Get-Rows).Count }
function Header-Names { @(Find-All 'HeaderItem' | ForEach-Object { $_.Current.Name }) }
function Selected-Index {
    $rows = Get-Rows
    for ($i = 0; $i -lt $rows.Count; $i++) {
        try { if ($rows[$i].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return $i } } catch { }
    }
    -1
}
function Select-Index([int]$index) {
    $rows = Get-Rows
    $rows[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $rows[$index].SetFocus(); Start-Sleep -Milliseconds 500
}
function First-Name {
    $row = (Get-Rows)[0]
    $A = $global:Kt.A
    $t = $row.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
    if ($t) { $t.Current.Name } else { '' }
}
function Open-ViewMenu {
    Set-Foreground
    $m = Find-AppMenuItem '表示(V)'
    if (-not $m) { Stop-Test 'View menu not found' }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 900
}
function Pick-Menu([string]$name) {
    $m = Find-AppMenuItem $name
    if (-not $m) { Stop-Test "menu item '$name' not found" }
    $m.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    # 項目が消える(メニューが閉じる)まで待つ。そのあと、少し置く
    [void](Wait-Until { -not (Find-AppMenuItem $name) } 2500 100)
    Start-Sleep -Milliseconds 1000
}

try {
    Start-KtApp
    Move-To $d
    Check '0) details view: rows are shown, with a column header' (((Row-Count) -ge 15) -and ((Header-Names) -contains '名前 ▲'))

    # ── 1) メニューで「アイコン」: 列見出しが消え、項目は同じ数。画像(サムネイル用)が並ぶ
    Open-ViewMenu; Pick-Menu '中アイコン'
    Check '1) icons view: no column headers' ((Header-Names).Count -eq 0)
    Check '1) icons view: items are shown' ((Row-Count) -ge 15)
    $images = @(Find-All 'Image' | Where-Object { -not $_.Current.IsOffscreen })
    Check '1) icons view: items show images (icons / thumbnails)' ($images.Count -ge 10)

    # ── 1b) 表示メニューの並び: 大アイコン / 中アイコン / 小アイコン / 一覧 / 詳細(アイコンの大きさのサブメニューは無い)
    Open-ViewMenu
    $ys = @('大アイコン', '中アイコン', '小アイコン', '一覧', '詳細') | ForEach-Object { $m = Find-AppMenuItem $_; if ($m) { $m.Current.BoundingRectangle.Y } else { -1 } }
    $inOrder = $true; for ($i = 1; $i -lt $ys.Count; $i++) { if ($ys[$i] -le $ys[$i - 1]) { $inOrder = $false } }
    Check '1b) view menu order: Large / Medium / Small icons, List, Details' (($ys -notcontains -1) -and $inOrder)
    Check '1b) the "icon size" submenu is gone' (-not (Find-AppMenuItem 'アイコンの大きさ'))
    Send-Keys '{ESC}' 500; Send-Keys '{ESC}' 500

    # ── 1c) 大アイコン・小アイコンで、アイコンの画像の大きさが変わる
    function Max-ImageWidth { (@(Find-All 'Image' | Where-Object { -not $_.Current.IsOffscreen } | ForEach-Object { $_.Current.BoundingRectangle.Width }) | Measure-Object -Maximum).Maximum }
    $mediumW = Max-ImageWidth
    Open-ViewMenu; Pick-Menu '大アイコン'
    $largeW = Max-ImageWidth
    Open-ViewMenu; Pick-Menu '小アイコン'
    $smallW = Max-ImageWidth
    Check "1c) icon sizes differ: large ($largeW) > medium ($mediumW) > small ($smallW)" (($largeW -gt $mediumW) -and ($mediumW -gt $smallW))
    Open-ViewMenu; Pick-Menu '中アイコン'

    # ── 2) アイコンでの矢印キー: 右=次、下=1行下(折り返した行の下)
    Select-Index 0
    Send-Keys '{RIGHT}' 600
    $afterRight = Selected-Index
    Check '2) icons view: Right moves to the next item' ($afterRight -eq 1)
    Send-Keys '{DOWN}' 600
    $afterDown = Selected-Index
    Check "2) icons view: Down moves one visual row down (index 1 -> $afterDown, more than 1 step)" ($afterDown -ge 4)
    Send-Keys '{UP}' 600
    Check '2) icons view: Up moves back one visual row' ((Selected-Index) -eq 1)
    Send-Keys '{LEFT}' 600
    Check '2) icons view: Left moves to the previous item' ((Selected-Index) -eq 0)

    # ── 3) アイコンでの名前の変更(その場で入力)
    # アイコン・一覧では、名前の入力欄が、画面要素(UI Automation)の一覧に出ないため、入力欄の中身や、フォーカスは、確かめられない。
    # そこで、Enter(入力欄にフォーカスが無いと、項目を開いてしまう)は使わず、入力のあと、ラベル(「絞込:」)のクリックで確定する
    # (入力欄にフォーカスが無ければ、何も変わらない)。
    Select-Index 0
    Send-Keys '{F2}' 1200
    Send-Text 'renamed'
    $A = $global:Kt.A
    $label = $global:Kt.Win.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)),
        (New-Object Windows.Automation.PropertyCondition($A::NameProperty, '絞込:')))))
    if (-not $label) { Stop-Test 'label not found' }
    Click-Element $label
    Check '3) icons view: rename in place works (typed in the box, committed by clicking elsewhere)' ((Test-Path -LiteralPath (Join-Path $d 'renamed.txt')) -and -not (Test-Path -LiteralPath (Join-Path $d 'f01.txt')))

    # ── 4) Ctrl+Shift+3 で「一覧」: 列で折り返す。右=次の列へ(1列の行数ぶん)、下=次
    Set-Foreground
    Send-Keys '^+3' 1800
    Check '4) list view: no column headers, items are shown' (((Header-Names).Count -eq 0) -and ((Row-Count) -ge 15))
    Select-Index 0
    Send-Keys '{DOWN}' 600
    Check '4) list view: Down moves to the next item' ((Selected-Index) -eq 1)
    Send-Keys '{RIGHT}' 600
    $afterRight = Selected-Index
    # 次の列へ動くと、選択した項目は、見えている範囲の外(画面要素に出ない = -1)になるか、7 以上の位置になる
    Check "4) list view: Right moves to the next column (index 1 -> $afterRight, i.e. beyond the next 5 items)" (($afterRight -eq -1) -or ($afterRight -ge 7))
    Send-Keys '{LEFT}' 600
    Check '4) list view: Left moves back one column' ((Selected-Index) -eq 1)

    # ── 5) メニューからの並べ替え(一覧には列見出しが無い): 名前の降順
    Open-ViewMenu
    $sort = Find-AppMenuItem '並べ替え'
    if (-not $sort) { Stop-Test 'sort menu not found' }
    $sort.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 700
    Pick-Menu '降順'

    # ── 6) Ctrl+Shift+1 で「詳細」へ戻る: 列見出しが戻り、並び順(名前 ▼、先頭が picture.png)も反映されている
    #    (アイコン・一覧では、項目の中身が画面要素に出ないため、並び順は、詳細に戻してから確かめる)
    # 自動操作でメニューの項目を押したあとは、メニューのキー操作の状態が残って、最初のキーが吸われることがある。詳細に戻るまで、最大 3 回送る
    for ($try = 0; $try -lt 3; $try++) {
        Set-Foreground
        Send-Keys '^+1' 600
        if (Wait-Until { (Header-Names) -contains '名前 ▼' } 2500 150) { break }
    }
    [void](Wait-Until { (Row-Count) -ge 15 } 3000 150)   # 詳細に戻ったあと、行が並ぶまで待つ
    Start-Sleep -Milliseconds 300
    $h = Header-Names
    Check "6) back to details: the column headers return with the sort arrow (headers: $($h -join ' / '))" (($h -contains '名前 ▼') -and ((Row-Count) -ge 15))
    # 降順の先頭: 手順3で名前を変えた renamed.txt(r は p より後ろ)
    Check "6) the sort chosen from the menu is applied: descending by name, the first item is renamed.txt (first = '$(First-Name)')" ((First-Name) -eq 'renamed.txt')
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
