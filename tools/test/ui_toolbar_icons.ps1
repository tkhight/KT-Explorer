# ツールバーのアイコンボタン(戻る・進む・一つ上・更新・元に戻す・やり直し・新しいタブ)の画面テスト:
# ボタンが、名前(読み上げ・自動操作用)つきで存在すること、アイコンが描かれていること(画素)、使えないときは灰色で、使えるときは濃い色になること、
# 押すと、それぞれの操作が働くこと(戻る・進む・一つ上・新しいタブ)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_toolbar_icons.ps1'
# 操作するのは %TEMP%\kt_test_ui_toolbar_icons の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。実行中は、キーボードとマウスに触らないこと。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Drawing

$d = New-TestDir 'kt_test_ui_toolbar_icons'
foreach ($n in 'a', 'b\inner') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }

$ids = [ordered]@{ BackButton = '戻る'; ForwardButton = '進む'; UpButton = '一つ上の階層へ'; RefreshButton = '更新'; UndoButton = '元に戻す'; RedoButton = 'やり直し'; NewTabButton = '新しいタブ' }

function Btn([string]$id) { Find-ById $id }
# ボタンの中(枠を除く)で、いちばん暗い画素の明るさ(0 = 黒、255 = 白)。アイコンが描かれていて、色が濃ければ小さい
function Darkest([string]$id) {
    $r = (Btn $id).Current.BoundingRectangle
    $x = [int]$r.X + 5; $y = [int]$r.Y + 5; $w = [int]$r.Width - 10; $h = [int]$r.Height - 10
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
    $min = 255
    for ($i = 0; $i -lt $w; $i++) { for ($j = 0; $j -lt $h; $j++) {
        $c = $bmp.GetPixel($i, $j); $l = [int](0.3 * $c.R + 0.59 * $c.G + 0.11 * $c.B); if ($l -lt $min) { $min = $l }
    } }
    $bmp.Dispose(); $min
}
function Invoke-Btn([string]$id) { (Btn $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }
function Tab-Count { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' }).Count }

try {
    Start-KtApp
    Move-To "$d\a"

    # ── 1) 7 つのボタンが、名前(AutomationProperties.Name)つきである
    foreach ($id in $ids.Keys) {
        $b = Btn $id
        Check "1) $id exists with the name '$($ids[$id])'" (($null -ne $b) -and ($b.Current.Name -eq $ids[$id]))
    }

    # ── 2) 使えるボタン(更新・新しいタブ)は、アイコンが描かれていて、色が濃い。使えないボタン(やり直し)は、灰色
    $dRefresh = Darkest 'RefreshButton'; $dNew = Darkest 'NewTabButton'
    Check "2) refresh draws a dark icon (darkest pixel: $dRefresh)" ($dRefresh -lt 80)
    Check "2) new-tab draws a dark icon (darkest pixel: $dNew)" ($dNew -lt 80)
    $redoBtn = Btn 'RedoButton'
    $dRedo = Darkest 'RedoButton'
    Check "2) redo is disabled now, and its icon is gray (enabled: $($redoBtn.Current.IsEnabled), darkest pixel: $dRedo)" ((-not $redoBtn.Current.IsEnabled) -and ($dRedo -ge 90) -and ($dRedo -lt 200))

    # ── 3) 移動すると、戻るが使える(濃い色)になり、押すと前のフォルダに戻る。進むが使えるようになる
    Move-To "$d\b"
    $back = Btn 'BackButton'
    Check '3) back is enabled after navigating' ($back.Current.IsEnabled)
    Check "3) back's icon turned dark (darkest pixel: $(Darkest 'BackButton'))" ((Darkest 'BackButton') -lt 80)
    Invoke-Btn 'BackButton'
    [void](Wait-Until { (Get-PathText) -eq "$d\a" } 3000 100)
    Check "3) pressing back returns to the previous folder ($(Get-PathText))" ((Get-PathText) -eq "$d\a")
    Check '3) forward is enabled after going back' ((Btn 'ForwardButton').Current.IsEnabled)
    Invoke-Btn 'ForwardButton'
    [void](Wait-Until { (Get-PathText) -eq "$d\b" } 3000 100)
    Check "3) pressing forward goes to the next folder ($(Get-PathText))" ((Get-PathText) -eq "$d\b")

    # ── 4) 一つ上: 押すと親のフォルダへ
    Move-To "$d\b\inner"
    Invoke-Btn 'UpButton'
    [void](Wait-Until { (Get-PathText) -eq "$d\b" } 3000 100)
    Check "4) pressing up goes to the parent folder ($(Get-PathText))" ((Get-PathText) -eq "$d\b")

    # ── 5) 新しいタブ: 押すとタブが増える
    $t0 = Tab-Count
    Invoke-Btn 'NewTabButton'
    [void](Wait-Until { (Tab-Count) -eq $t0 + 1 } 3000 100)
    Check "5) pressing new-tab adds a tab ($t0 -> $(Tab-Count))" ((Tab-Count) -eq $t0 + 1)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
