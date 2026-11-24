# オーバーレイアイコンの画面テスト: 一覧のアイコンに、リンクの矢印(シェルのオーバーレイ)が重なって出ること、
# 「表示」メニューの「オーバーレイアイコンを表示」のオフ/オンで、出たり消えたりすること(アイコンの画素を比べる)。
# OneDrive・SVN のハンドラーが無い環境でも確かめられるように、ジャンクション(mklink /J)のフォルダの矢印で見る
# (ショートカット .lnk は、アイコン自体に矢印が含まれていて、オーバーレイのオフでも消えないので使えない)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_overlay.ps1'
# 操作するのは %TEMP%\kt_test_ui_overlay の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。実行中は、キーボードとマウスに触らないこと。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_overlay'
New-Item -ItemType Directory (Join-Path $d 'target') | Out-Null
cmd /c "mklink /J `"$d\junc`" `"$d	arget`"" | Out-Null   # ジャンクション(リンクの矢印のオーバーレイが付く)

function Get-Rows {
    $r = @(Find-All 'DataItem')
    if ($r.Count -eq 0) {
        $top = $global:Kt.Win.Current.BoundingRectangle.Y + 125
        $r = @(Find-All 'ListItem' | Where-Object { $_.Current.BoundingRectangle.Y -gt $top })
    }
    $r
}
function Find-Row([string]$name) {
    $A = $global:Kt.A
    foreach ($row in Get-Rows) {
        $t = $row.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
        if ($t -and $t.Current.Name -eq $name) { return $row }
    }
}
# 行の左端(アイコンの部分)の画像を撮る
function Grab-Icon($row) {
    $rc = $row.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap(24, [int][Math]::Min($rc.Height, 24))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$rc.X, [int]$rc.Y, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp
}
function Diff-Pixels($a, $b) {
    $n = 0
    for ($x = 0; $x -lt $a.Width; $x++) {
        for ($y = 0; $y -lt [Math]::Min($a.Height, $b.Height); $y++) {
            $p = $a.GetPixel($x, $y); $q = $b.GetPixel($x, $y)
            if ([Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B) -gt 60) { $n++ }
        }
    }
    $n
}
function Toggle-Overlay {
    Set-Foreground
    $m = Find-AppMenuItem '表示(V)'
    if (-not $m) { Stop-Test "menu '表示(V)' not found" }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 900
    $item = Find-AppMenuItem 'オーバーレイアイコンを表示'
    if (-not $item) { Stop-Test 'menu item "オーバーレイアイコンを表示" not found' }
    $state = "$($item.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState)"
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 700
    Send-Keys '{ESC}' 500; Send-Keys '{ESC}' 500
    $state
}

try {
    Start-KtApp
    Move-To $d
    Start-Sleep -Milliseconds 2500   # オーバーレイは、バックグラウンドで調べて、あとから出る

    $link = Find-Row 'junc'; $plain = Find-Row 'target'
    if (-not $link -or -not $plain) { Stop-Test 'the rows (junc / target) were not found' }
    $linkOn = Grab-Icon $link; $plainOn = Grab-Icon $plain

    # ── 1) オフにする: メニューに項目があり、最初はオン(チェック済み)。オフにすると、ショートカットのアイコンだけが変わる
    $before = Toggle-Overlay
    Check "1) the menu item exists and starts checked (was: $before)" ($before -eq 'On')
    Start-Sleep -Milliseconds 1200
    $link = Find-Row 'junc'; $plain = Find-Row 'target'
    $linkOff = Grab-Icon $link; $plainOff = Grab-Icon $plain
    $dLink = Diff-Pixels $linkOn $linkOff
    $dPlain = Diff-Pixels $plainOn $plainOff
    Check "1) turning it off changes the junction's icon (the arrow disappears; $dLink pixels differ)" ($dLink -ge 6)
    Check "1) turning it off does not change a plain folder's icon ($dPlain pixels differ)" ($dPlain -le 2)

    # ── 2) オンに戻す: 矢印がまた出る(最初と同じ見た目)
    $state = Toggle-Overlay
    Check "2) the menu item was off before turning it on again (was: $state)" ($state -eq 'Off')
    Start-Sleep -Milliseconds 2500
    $linkAgain = Grab-Icon (Find-Row 'junc')
    $dBack = Diff-Pixels $linkOn $linkAgain
    Check "2) turning it on again brings the arrow back (same as at first; $dBack pixels differ)" ($dBack -le 2)
}
finally {
    Stop-KtApp
    cmd /c "rmdir `"$d\junc`"" | Out-Null   # ジャンクションは、先にリンクだけを外す(中身を消さないため)
    Remove-TestDir $d
}
Show-Result
