# ペインの幅(スプリッターのドラッグ)と、ペインを増やしたあとも幅を保つことの画面テスト。
# アプリ本体は起動せず、ペインの表示部品(PaneTabView)だけを専用のウィンドウに載せる。
# そのため、KT_Explorer が起動中でも実行できる(ユーザーのアプリには触れない)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_pane_width.ps1'
# 表示するフォルダは %TEMP%\kt_test_pane の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Initialize-Ui
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore

$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$PV = $asm.GetType('KT_Explorer.Views.PaneTabView')
$PT = $asm.GetType('KT_Explorer.ViewModels.PaneTabViewModel')

$d = New-TestDir 'kt_test_pane'
foreach ($n in 'p1', 'p2', 'p3') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
$w = $null
try {
    $paths = [string[]]@("$d\p1", "$d\p2")
    $vm = [Activator]::CreateInstance($PT, [object[]]@(,$paths))
    $view = [Activator]::CreateInstance($PV); $view.DataContext = $vm
    $w = New-Object System.Windows.Window
    $w.Width = 1200; $w.Height = 600; $w.Left = 50; $w.Top = 50; $w.Content = $view; $w.Show()
    function Pump { $w.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Background) | Out-Null }
    Pump; Pump
    $host_ = $view.FindName('Host')
    function Widths { @($host_.ColumnDefinitions | Where-Object { $_.MinWidth -gt 0 } | ForEach-Object { [math]::Round($_.ActualWidth) }) }
    function Splitters { @($host_.Children | Where-Object { $_ -is [System.Windows.Controls.GridSplitter] }) }

    $w0 = Widths
    Check "2 panes start equal (widths $($w0 -join ' / '))" ([math]::Abs($w0[0] - $w0[1]) -le 2)

    # スプリッターを、実際のマウスで右へ 200px ドラッグ
    $sp = (Splitters)[0]
    $pt = $sp.PointToScreen([System.Windows.Point]::new($sp.ActualWidth / 2, $sp.ActualHeight / 2))
    [void][KtWin]::SetCursorPos([int]$pt.X, [int]$pt.Y); Start-Sleep -Milliseconds 300; Pump
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 100; Pump
    foreach ($dx in 50, 100, 150, 200) { [void][KtWin]::SetCursorPos([int]$pt.X + $dx, [int]$pt.Y); Start-Sleep -Milliseconds 80; Pump }
    [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200; Pump; Pump
    $w1 = Widths
    Check "dragging the splitter widens the first pane (widths $($w1 -join ' / '))" ($w1[0] -gt $w1[1] + 150)

    # ペインを1つ増やす: 既存の2つは、ドラッグした比率を保ち、新しいペインは平均の幅
    [void]$vm.AddPane("$d\p3"); Pump; Pump; Pump
    $w2 = Widths
    $ratioBefore = $w1[0] / $w1[1]
    $ratioAfter = $w2[0] / $w2[1]
    Check "adding a pane keeps the ratio of the existing panes (before $([math]::Round($ratioBefore, 2)), after $([math]::Round($ratioAfter, 2)); widths $($w2 -join ' / '))" ([math]::Abs($ratioBefore - $ratioAfter) -lt 0.1)
    $avg = ($w2[0] + $w2[1]) / 2
    Check 'the new pane gets the average width of the existing panes' ([math]::Abs($w2[2] - $avg) -lt $avg * 0.15)

    # ペインを1つ閉じても、残りの比率は保たれる
    $vm.RemovePane($vm.Panes[2]); Pump; Pump; Pump
    $w3 = Widths
    Check "closing a pane keeps the ratio of the remaining panes (widths $($w3 -join ' / '))" ([math]::Abs(($w3[0] / $w3[1]) - $ratioBefore) -lt 0.1)
}
finally {
    if ($w) { $w.Close() }
    Remove-TestDir $d
}
Show-Result
