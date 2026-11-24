# マウスジェスチャ「右ボタンを押したままホイール」(上=左のタブ・下=右のタブ)(WheelGestureTracker)のロジックの検証。画面は出さない。
# (実際のマウス入力での確認は、マウスジェスチャ系のツールが常駐している PC では、合成した右ボタンが横取りされて、できない。)
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_gesture.ps1'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$TrackerType = $asm.GetType('KT_Explorer.Services.WheelGestureTracker')

function New-Tracker { [System.Activator]::CreateInstance($TrackerType) }
function Wheel($t, [int]$delta) { "$($t.Wheel($delta))" }

$t = New-Tracker
Check 'wheel without the right button does nothing' ((Wheel $t 120) -eq 'None')
$t.RightDown($true)
Check 'right button + wheel up = PreviousTab (the tab on the left)' ((Wheel $t 120) -eq 'PreviousTab')
Check 'right button + wheel down = NextTab (the tab on the right)' ((Wheel $t -120) -eq 'NextTab')
Check 'a wheel delta of 0 does nothing' ((Wheel $t 0) -eq 'None')
Check 'releasing after using the wheel suppresses the context menu' ($t.RightUp() -eq $true)
Check 'after the release, the wheel does nothing again' ((Wheel $t 120) -eq 'None')

$t = New-Tracker
$t.RightDown($true)
Check 'releasing without using the wheel does not suppress the menu (a plain right click)' ($t.RightUp() -eq $false)

$t = New-Tracker
$t.RightDown($false)
Check 'gestures switched off: the wheel does nothing' ((Wheel $t 120) -eq 'None')
Check 'gestures switched off: nothing is suppressed' ($t.RightUp() -eq $false)

$t = New-Tracker
$t.RightDown($true); [void](Wheel $t 120)
$t.RightDown($true)
Check 'pressing again starts fresh (the earlier use does not carry over)' ($t.RightUp() -eq $false)

$t = New-Tracker
$t.RightDown($true); [void](Wheel $t 120)
$t.Cancel()
Check 'cancel (window deactivated / a missed release): the wheel does nothing and nothing is suppressed' (((Wheel $t 120) -eq 'None') -and ($t.RightUp() -eq $false) -and (-not $t.IsHeld))

$t = New-Tracker
$t.RightDown($true)
$many = 1..5 | ForEach-Object { Wheel $t 120 }
Check 'several notches in one press each count' (($many | Where-Object { $_ -eq 'PreviousTab' }).Count -eq 5)
# ── Ctrl つき: 前・次のタブグループ
$t = New-Tracker
$t.RightDown($true)
$up = "$($t.Wheel(120, $true))"; $down = "$($t.Wheel(-120, $true))"
Check 'Ctrl + right button + wheel up = PreviousGroup, down = NextGroup' (($up -eq 'PreviousGroup') -and ($down -eq 'NextGroup'))
Check 'releasing after a Ctrl wheel also suppresses the context menu' ($t.RightUp() -eq $true)
$t = New-Tracker
Check 'Ctrl + wheel without the right button does nothing' ("$($t.Wheel(120, $true))" -eq 'None')

# ── ロッカー(右ボタンを押したまま左クリック = タブを閉じる)
$t = New-Tracker
$t.RightDown($true)
Check 'rocker: left click while the right button is held -> true (close the tab)' ($t.Rocker($true) -eq $true)
Check 'rocker: releasing the right button afterwards suppresses the context menu' ($t.RightUp() -eq $true)
$t = New-Tracker
$t.RightDown($true)
Check 'rocker: left click when the right button is no longer pressed (a missed release) -> false' ($t.Rocker($false) -eq $false)
Check 'rocker: a missed release cancels tracking (no menu suppression, not held)' (($t.RightUp() -eq $false) -and (-not $t.IsHeld))
$t = New-Tracker
Check 'rocker: left click without the right button does nothing' (($t.Rocker($true) -eq $false) -and (-not $t.IsHeld))
$t = New-Tracker
$t.RightDown($false)
Check 'rocker: gestures switched off -> never closes a tab' ($t.Rocker($true) -eq $false)
$t = New-Tracker
$t.RightDown($true)
Check 'rocker: several left clicks in one press each close a tab' (($t.Rocker($true) -eq $true) -and ($t.Rocker($true) -eq $true))
Show-Result
