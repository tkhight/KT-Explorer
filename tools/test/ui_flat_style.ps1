# 画面全体のフラットなデザインの画面テスト(枠なしのアイコンボタン・ホバー・オンの色・グループタブ・フォルダタブ・ペインのボタン)。
# 画素の色で確かめる: 枠・背景なし(まわりと同じ色)/ ホバー = 淡い水色(#E5F3FF)/ オン = 濃い水色(#CCE8FF)/ 選択中のグループ = #DCEBFA /
# 選択中のフォルダタブ = 白い背景 + 上に青い線(#3B82D6)。ペインのボタンは、押すとペインタブができる。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_flat_style.ps1'
# 操作するのは %TEMP%\kt_test_ui_flat_style の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。マウスは実際に動かす(テスト中は触らない)。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_flat_style'
foreach ($n in 'a', 'b') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }

function Pixel([int]$x, [int]$y) {
    $bmp = New-Object System.Drawing.Bitmap(1, 1)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
    $c = $bmp.GetPixel(0, 0); $bmp.Dispose(); $c
}
function Near($c, [int]$r, [int]$g, [int]$b, [int]$tol = 8) { ([Math]::Abs($c.R - $r) -le $tol) -and ([Math]::Abs($c.G - $g) -le $tol) -and ([Math]::Abs($c.B - $b) -le $tol) }
function Rect([string]$id) { (Find-ById $id).Current.BoundingRectangle }
function Same-Color($a, $b) { ([Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B)) -le 6 }
function Move-Mouse([int]$x, [int]$y) { Assert-AppForeground; [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 400 }
function Test-Selected($el) { try { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false } }
function Selected-Item([string]$typeName) { Find-All 'ListItem' | Where-Object { $_.Current.Name -like "*$typeName*" } | Where-Object { Test-Selected $_ } | Select-Object -First 1 }

try {
    Start-KtApp
    Move-To "$d\a"
    Set-Foreground; Send-Keys '^t' 300; Move-To "$d\b"

    # ── 1) ボタンが、名前つきで存在する
    foreach ($id in 'Panes2Button', 'Panes3Button', 'SpecifyPaneButton', 'PreviewToggle', 'AddGroupButton', 'NewTabButton', 'RefreshButton') {
        Check "1) $id exists" ($null -ne (Find-ById $id))
    }

    # ── 2) 枠なし・背景なし: ボタンの内側の端の色が、すぐ外側(ツールバーの背景)と同じ
    $r = Rect 'RefreshButton'
    $inside = Pixel ([int]$r.X + 2) ([int]$r.Y + [int]($r.Height / 2))
    $outside = Pixel ([int]$r.X - 5) ([int]$r.Y + [int]($r.Height / 2))
    Check "2) a toolbar button has no border and no background (inside $($inside.R),$($inside.G),$($inside.B) = outside $($outside.R),$($outside.G),$($outside.B))" (Same-Color $inside $outside)

    # ── 3) ホバー: カーソルを乗せると、淡い水色(#E5F3FF)になる
    Move-Mouse ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    $hover = Pixel ([int]$r.X + 4) ([int]$r.Y + [int]($r.Height / 2))
    Check "3) hovering turns the button light blue (got $($hover.R),$($hover.G),$($hover.B))" (Near $hover 229 243 255 6)
    Move-Mouse ([int]($r.X + $r.Width / 2)) ([int]($r.Y + 80))

    # ── 4) プレビューの切り替え: オンで濃い水色(#CCE8FF)、オフで元に戻る
    $pr = Rect 'PreviewToggle'
    $off = Pixel ([int]$pr.X + 4) ([int]$pr.Y + [int]($pr.Height / 2))
    Check "4) the preview toggle is plain when off (got $($off.R),$($off.G),$($off.B))" (Same-Color $off $outside)
    (Find-ById 'PreviewToggle').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 700
    Move-Mouse ([int]($pr.X + $pr.Width / 2)) ([int]($pr.Y + 90))
    $on = Pixel ([int]$pr.X + 4) ([int]$pr.Y + [int]($pr.Height / 2))
    Check "4) the preview toggle turns darker blue when on (got $($on.R),$($on.G),$($on.B))" (Near $on 204 232 255 8)
    (Find-ById 'PreviewToggle').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 700

    # ── 5) 選択中のグループタブは薄い青(#DCEBFA)の背景
    $gi = Selected-Item 'TabGroup'
    $gr = $gi.Current.BoundingRectangle
    $gp = Pixel ([int]$gr.X + 3) ([int]$gr.Y + [int]($gr.Height / 2))
    Check "5) the selected group tab has a light blue background (got $($gp.R),$($gp.G),$($gp.B))" (Near $gp 220 235 250 8)

    # ── 6) 選択中のフォルダタブは、白い背景で、上に青い線(#3B82D6)
    $ti = Selected-Item 'FolderTab'
    $tr = $ti.Current.BoundingRectangle
    $top = Pixel ([int]($tr.X + $tr.Width / 2)) ([int]$tr.Y + 1)
    $body = Pixel ([int]($tr.X + $tr.Width) - 4) ([int]$tr.Y + [int]($tr.Height / 2))   # 右端の余白(中身の無い所)
    Check "6) the selected folder tab has a blue line on top (got $($top.R),$($top.G),$($top.B))" (Near $top 59 130 214 14)
    Check "6) and a white background (got $($body.R),$($body.G),$($body.B))" (Near $body 255 255 255 4)

    # ── 7) ペインのボタン(2 ペイン)を押すと、ペインタブができる
    $before = @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*PaneTab*' }).Count
    (Find-ById 'Panes2Button').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-Until { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*PaneTab*' }).Count -eq $before + 1 } 3000 100)
    Check '7) the 2-pane button creates a pane tab' (@(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*PaneTab*' }).Count -eq $before + 1)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
