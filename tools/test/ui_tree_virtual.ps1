# ツリーの仮想化の画面テスト: サブフォルダが多数あるフォルダでも、画面の要素は見えている分だけが作られること(要素の数)、
# 選択したフォルダのノードが、ツリーの表示範囲の中へスクロールされること(深い・遠いノードを開いても、見える位置に来る)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_tree_virtual.ps1'
# 操作するのは %TEMP%\kt_test_ui_tree_virtual の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。実行中は、キーボードとマウスに触らないこと。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_tree_virtual'
1..300 | ForEach-Object { New-Item -ItemType Directory (Join-Path $d ('f{0:000}' -f $_)) | Out-Null }
New-Item -ItemType Directory (Join-Path $d 'f250\inner') | Out-Null

function Tree-Rect { (Find-All 'Tree' | Select-Object -First 1).Current.BoundingRectangle }
function Selected-Node {
    Find-All 'TreeItem' | Where-Object {
        try { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false }
    } | Select-Object -First 1
}

try {
    Start-KtApp
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Move-To "$d\f250"
    Start-Sleep -Milliseconds 1500

    # ── 1) 300 個のサブフォルダがあっても、画面の要素(TreeItem)は、見えている分だけ(余分に作る分を含めても、数十個)
    $items = @(Find-All 'TreeItem')
    Check "1) the tree creates only the visible nodes ($($items.Count) tree items, though the folder has 300 subfolders)" ($items.Count -lt 120)

    # ── 2) 選択したフォルダ(f250)のノードが、ツリーの表示範囲の中にある(スクロールされている)
    $tr = Tree-Rect
    $sel = Selected-Node
    Check '2) the selected node exists on screen (it was brought into view)' ($null -ne $sel)
    if ($sel) {
        $r = $sel.Current.BoundingRectangle
        Check "2) the selected node is inside the visible tree area (y=$([int]$r.Y), tree y=$([int]$tr.Y)..$([int]($tr.Y + $tr.Height)))" (($r.Y -ge $tr.Y) -and (($r.Y + $r.Height) -le ($tr.Y + $tr.Height + 1)))
    }

    # ── 3) 遠いノードへ移っても、また見える位置に来る(f010 → f290)
    Move-To "$d\f010"; Start-Sleep -Milliseconds 1200
    $sel = Selected-Node; $tr = Tree-Rect
    $ok10 = $sel -and ($sel.Current.BoundingRectangle.Y -ge $tr.Y) -and (($sel.Current.BoundingRectangle.Y + 20) -le ($tr.Y + $tr.Height + 1))
    Check '3) moving to a node near the top scrolls it into view' ([bool]$ok10)
    Move-To "$d\f290"; Start-Sleep -Milliseconds 1200
    $sel = Selected-Node; $tr = Tree-Rect
    $ok290 = $sel -and ($sel.Current.BoundingRectangle.Y -ge $tr.Y) -and (($sel.Current.BoundingRectangle.Y + 20) -le ($tr.Y + $tr.Height + 1))
    Check '3) moving to a node near the bottom scrolls it into view' ([bool]$ok290)

    # ── 4) 要素の数は、移動を繰り返しても増え続けない
    $items2 = @(Find-All 'TreeItem')
    Check "4) the number of tree items stays small after moving around ($($items2.Count))" ($items2.Count -lt 120)

    # ── 5) 画面要素の列挙が速い(仮想化前は、1 回に約 9 秒かかった)
    $t = [Diagnostics.Stopwatch]::StartNew(); [void](Find-All 'ListItem'); $t.Stop()
    Check "5) enumerating the window's elements is fast ($($t.ElapsedMilliseconds) ms)" ($t.ElapsedMilliseconds -lt 2500)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
