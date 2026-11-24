# オーバーレイアイコン(シェルのオーバーレイ ハンドラーが決める。OneDrive・SVN・ショートカットの矢印など)のロジックの検証。画面は出さない。
# テスト環境に OneDrive・SVN は要らないので、どの環境にもある「ショートカット(.lnk)の矢印」で確かめる。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_overlay.ps1'
# 操作するフォルダは %TEMP%\kt_test_logic_overlay の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$OS = $asm.GetType('KT_Explorer.Services.OverlayIconService')
$FE = $asm.GetType('KT_Explorer.Models.FileEntry')
$AS = $asm.GetType('KT_Explorer.Services.AppState')

function Get-Overlay([string]$path) {
    $t = $OS.GetMethod('GetAsync').Invoke($null, [object[]]@($path))
    if (-not $t.Wait(10000)) { return 'TIMEOUT' }
    $t.Result
}

$d = New-TestDir 'kt_test_logic_overlay'
try {
    Set-Content -LiteralPath "$d\plain.txt" -Value 'x'
    New-Item -ItemType Directory "$d\folder" | Out-Null
    # ショートカット(.lnk)を作る
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut("$d\link.lnk"); $lnk.TargetPath = "$d\plain.txt"; $lnk.Save()

    $OS.GetProperty('Enabled').SetValue($null, $true)

    # ── 1) 普通のファイル・フォルダには、オーバーレイが無い(OneDrive・SVN の外)
    Check '1) a plain file has no overlay' ($null -eq (Get-Overlay "$d\plain.txt"))
    Check '1) a plain folder has no overlay' ($null -eq (Get-Overlay "$d\folder"))

    # ── 2) ショートカットには、矢印のオーバーレイがある(小・大の両方)
    $o = Get-Overlay "$d\link.lnk"
    Check '2) a shortcut has an overlay (the arrow)' (($null -ne $o) -and ($o -ne 'TIMEOUT'))
    if ($o -and $o -ne 'TIMEOUT') {
        Check "2) the small overlay is an image ($($o.Small.PixelWidth)px) and frozen" (($o.Small.PixelWidth -ge 8) -and $o.Small.IsFrozen)
        Check "2) the large overlay is bigger than the small one ($($o.Large.PixelWidth)px)" ($o.Large.PixelWidth -gt $o.Small.PixelWidth)
        $o2 = Get-Overlay "$d\link.lnk"
        Check '2) the same overlay kind reuses the same images' ([object]::ReferenceEquals($o.Small, $o2.Small))
    }

    # ── 3) 存在しないパス・壊れたパスでも、落ちない(null)
    Check '3) a path that does not exist gives no overlay (no crash)' ($null -eq (Get-Overlay "$d\nope\x.txt"))

    # ── 4) 一覧の項目(FileEntry): 最初に参照したときにバックグラウンドで調べて、できたら通知する
    $entry = [Activator]::CreateInstance($FE, [object[]]@([System.IO.FileInfo]::new("$d\link.lnk"), $null))
    $first = $entry.OverlayIcon
    Check '4) first read does not block (nothing yet)' ($null -eq $first)
    for ($i = 0; $i -lt 50 -and $null -eq $entry.OverlayIcon; $i++) { Start-Sleep -Milliseconds 100 }
    Check '4) the overlay arrives in the background' ($null -ne $entry.OverlayIcon)
    Check '4) the large overlay is available too' ($null -ne $entry.OverlayIconLarge)
    $plain = [Activator]::CreateInstance($FE, [object[]]@([System.IO.FileInfo]::new("$d\plain.txt"), $null))
    [void]$plain.OverlayIcon; Start-Sleep -Milliseconds 1500
    Check '4) a plain file stays without overlay' ($null -eq $plain.OverlayIcon)

    # ── 5) 再読み込みで調べ直しても、いまのオーバーレイは消えない(ちらつかない)
    $entry.RefreshOverlay(); Start-Sleep -Milliseconds 200
    Check '5) refreshing keeps the current overlay until the new result arrives' ($null -ne $entry.OverlayIcon)

    # ── 6) オフ: 調べない(null)。項目の ResetOverlay で消え、オフのままなら出てこない。オンに戻すと、また出る
    $OS.GetProperty('Enabled').SetValue($null, $false)
    Check '6) off: the service gives no overlay' ($null -eq (Get-Overlay "$d\link.lnk"))
    $entry.ResetOverlay()
    Check '6) off: ResetOverlay clears it' ($null -eq $entry.OverlayIcon)
    Start-Sleep -Milliseconds 500
    Check '6) off: it stays empty' ($null -eq $entry.OverlayIcon)
    $OS.GetProperty('Enabled').SetValue($null, $true)
    $entry.ResetOverlay()
    for ($i = 0; $i -lt 50 -and $null -eq $entry.OverlayIcon; $i++) { [void]$entry.OverlayIcon; Start-Sleep -Milliseconds 100 }
    Check '6) on again: the overlay comes back' ($null -ne $entry.OverlayIcon)

    # ── 7) 設定の保存: 既定はオン
    $st = [Activator]::CreateInstance($AS)
    Check '7) the setting is on by default' ($st.ShowOverlayIcons -eq $true)
}
finally {
    $OS.GetProperty('Enabled').SetValue($null, $true)
    Remove-TestDir $d
}
Show-Result
