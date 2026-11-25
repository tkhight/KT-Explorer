# ツリーとタブのアイコン(フォルダのアイコン・固有のアイコン・オーバーレイ)のロジックの検証。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_navicon.ps1'
# 操作するフォルダは %TEMP%\kt_test_logic_navicon の中だけ。ジャンクション(リンクの矢印のオーバーレイが付く)を使う。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$SIS = $asm.GetType('KT_Explorer.Services.ShellIconService')
$OS  = $asm.GetType('KT_Explorer.Services.OverlayIconService')
$TNV = $asm.GetType('KT_Explorer.ViewModels.TreeNodeViewModel')
$FTV = $asm.GetType('KT_Explorer.ViewModels.FolderTabViewModel')
$PTV = $asm.GetType('KT_Explorer.ViewModels.PaneTabViewModel')
$MVM = $asm.GetType('KT_Explorer.ViewModels.MainViewModel')
$AS  = $asm.GetType('KT_Explorer.Services.AppState')

function Wait-Until([scriptblock]$cond, [int]$ms = 6000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 100 }
    $false
}

$d = New-TestDir 'kt_test_logic_navicon'
try {
    foreach ($n in 'plain', 'target', 'plain\child') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
    cmd /c "mklink /J `"$d\junc`" `"$d\target`"" | Out-Null
    $OS.GetProperty('Enabled').SetValue($null, $true)
    function HasOwn([string]$p) { $SIS.GetMethod('HasOwnIcon').Invoke($null, [object[]]@($p)) }
    function FolderIcon([string]$p) { $SIS.GetMethod('GetIcon').Invoke($null, [object[]]@($p, $true)) }

    # ── 1) 固有のアイコンを持つフォルダ: ドライブのルート・デスクトップなど。通常のフォルダは、共通のアイコン
    Check '1) a drive root has its own icon' ((HasOwn 'C:\') -and (HasOwn 'C:'))
    Check '1) the desktop has its own icon' (HasOwn ([Environment]::GetFolderPath('DesktopDirectory')))
    Check '1) the Downloads folder has its own icon' (HasOwn (Join-Path $env:USERPROFILE 'Downloads'))
    Check '1) a normal folder uses the shared folder icon' (-not (HasOwn "$d\plain"))
    $i1 = FolderIcon "$d\plain"; $i2 = FolderIcon "$d\plain\child"
    Check '1) normal folders share one icon object (cached)' (($null -ne $i1) -and [object]::ReferenceEquals($i1, $i2))
    $drive = FolderIcon 'C:\'
    Check '1) the drive icon is a different image from the folder icon' (($null -ne $drive) -and (-not [object]::ReferenceEquals($drive, $i1)))

    # ── 2) ツリーのノード: アイコンがある。ダミーの子には無い。ジャンクションのノードには、オーバーレイが後から付く
    $cb = [Action[object]]{ param($x) }
    $node = [Activator]::CreateInstance($TNV, [object[]]@('plain', "$d\plain", $cb, $null))
    Check '2) a tree node has an icon' ($null -ne $node.Icon)
    Check '2) the placeholder child has no icon' ($null -eq $node.Children[0].Icon)
    Check '2) a normal folder node has no overlay' (-not (Wait-Until { $null -ne $node.OverlayIcon } 1500))
    $jn = [Activator]::CreateInstance($TNV, [object[]]@('junc', "$d\junc", $cb, $null))
    [void]$jn.OverlayIcon
    Check '2) a junction node gets the link overlay (in the background)' (Wait-Until { $null -ne $jn.OverlayIcon })
    $jn.ResetOverlay()
    $OS.GetProperty('Enabled').SetValue($null, $false)
    $jn.ResetOverlay()
    Check '2) with the overlay off, the node has no overlay' (-not (Wait-Until { $null -ne $jn.OverlayIcon } 1500))
    $OS.GetProperty('Enabled').SetValue($null, $true)
    $jn.ResetOverlay()
    Check '2) turned on again, the overlay comes back' (Wait-Until { [void]$jn.OverlayIcon; $null -ne $jn.OverlayIcon })

    # ── 3) タブ(フォルダタブ): アイコンがある。パスが変わるとアイコン・オーバーレイが付け替わる
    $tab = [Activator]::CreateInstance($FTV, [object[]]@("$d\plain"))
    Check '3) a folder tab has an icon' ($null -ne $tab.Icon)
    $changed = New-Object System.Collections.Generic.List[string]
    $tab.add_PropertyChanged([System.ComponentModel.PropertyChangedEventHandler]{ param($s, $e) $changed.Add($e.PropertyName) })
    $tab.NavigateTo("$d\junc")
    Check '3) navigating notifies that the icon changed' ($changed -contains 'Icon')
    Check '3) a tab on a junction gets the link overlay' (Wait-Until { [void]$tab.OverlayIcon; $null -ne $tab.OverlayIcon })
    $tab.NavigateTo("$d\plain")
    Check '3) moving to a normal folder clears the overlay at once' ($null -eq $tab.OverlayIcon)
    Check '3) and it stays clear (a late result for the old folder is dropped)' (-not (Wait-Until { $null -ne $tab.OverlayIcon } 1500))
    $drv = [Activator]::CreateInstance($FTV, [object[]]@('C:\'))
    Check '3) a tab on a drive root shows the drive icon' (([object]::ReferenceEquals($drv.Icon, $drive)) -and ($null -ne $drv.Icon))

    # ── 4) ペインタブには、アイコンを付けない(タイトルの ▥ で表す)
    $pane = [Activator]::CreateInstance($PTV, [object[]]@(, [string[]]@("$d\plain", "$d\target")))
    Check '4) a pane tab has no icon (the title starts with ▥)' (($null -eq $pane.Icon) -and ($pane.Title -like '▥*'))

    # ── 5) 「オーバーレイを表示」の切り替えは、タブとツリーにも効く
    $vm = [Activator]::CreateInstance($MVM, [object[]]@([Activator]::CreateInstance($AS)))
    $vm.OpenInNewTab("$d\junc"); $jt = $vm.SelectedTab
    Check '5) a tab opened on a junction has the overlay' (Wait-Until { [void]$jt.OverlayIcon; $null -ne $jt.OverlayIcon })
    $vm.ShowOverlayIcons = $false
    Check '5) turning the setting off removes the overlay from tabs' ($null -eq $jt.OverlayIcon)
    $vm.ShowOverlayIcons = $true
    Check '5) turning it on again brings it back' (Wait-Until { [void]$jt.OverlayIcon; $null -ne $jt.OverlayIcon })
}
finally {
    $OS.GetProperty('Enabled').SetValue($null, $true)
    cmd /c "rmdir `"$d\junc`"" | Out-Null
    Remove-TestDir $d
}
Show-Result
