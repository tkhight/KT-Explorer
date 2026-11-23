# アプリのアイコン(app.ico)のロジックの検証: .ico の中身(大きさの種類)、exe のアイコン、トレイのアイコンの読み込み。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_icon.ps1'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore, System.Drawing, System.Windows.Forms
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$icoPath = Join-Path $repo 'src\KT_Explorer\Resources\app.ico'

# ── .ico の中身: 16 / 24 / 32 / 48 / 64 / 128 / 256 が入っている
Check 'app.ico exists' (Test-Path -LiteralPath $icoPath)
$bytes = [System.IO.File]::ReadAllBytes($icoPath)
$count = [BitConverter]::ToUInt16($bytes, 4)
$sizes = @(0..($count - 1) | ForEach-Object { $w = $bytes[6 + 16 * $_]; if ($w -eq 0) { 256 } else { $w } })
Check "app.ico: header is a valid ICO (type 1) with $count images ($($sizes -join ', '))" (([BitConverter]::ToUInt16($bytes, 0) -eq 0) -and ([BitConverter]::ToUInt16($bytes, 2) -eq 1) -and ($sizes.Count -eq 7))
Check 'app.ico: has the sizes Windows asks for (16, 32, 48, 256)' ((16, 32, 48, 256 | Where-Object { $sizes -contains $_ }).Count -eq 4)

# ── exe のアイコン(エクスプローラー・タスクバー): 既定のアイコンではなく、このアイコン
$exe = $global:Kt.Exe
$exeIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($exe)
function Has-AppBlue($bmp) {
    $found = $false
    for ($y = 0; $y -lt $bmp.Height -and -not $found; $y++) { for ($x = 0; $x -lt $bmp.Width; $x++) { $c = $bmp.GetPixel($x, $y); if ($c.A -gt 200 -and $c.B -gt 180 -and $c.R -lt 120 -and $c.G -gt 100) { $found = $true; break } } }
    $found
}
Check 'exe icon: it is the app icon (blue folder), not the default exe icon' (Has-AppBlue $exeIcon.ToBitmap())

# ── トレイのアイコン(TrayService.CreateIcon): リソースから読めている
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$tray = $asm.GetType('KT_Explorer.Services.TrayService')
$m = $tray.GetMethod('CreateIcon', [Reflection.BindingFlags]'NonPublic,Static')
# リソースの読み込みには、WPF の Application が要る。このプロセスに作る(1 つだけ)
if (-not [System.Windows.Application]::Current) { $null = New-Object System.Windows.Application }
$trayIcon = $m.Invoke($null, @())
Check 'tray icon: an icon object is created' ($null -ne $trayIcon)
Check 'tray icon: it is the app icon (blue folder), not the default application icon' (Has-AppBlue $trayIcon.ToBitmap())
Show-Result
