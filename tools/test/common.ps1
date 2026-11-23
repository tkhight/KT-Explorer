# KT_Explorer 画面テスト・ロジックテストの共通部品(各テストスクリプトから . で読み込んで使う)
#
# 安全のための決まり(変更するときも守ること):
#   - すでに KT_Explorer が起動していたら中止する(ユーザーが使用中のインスタンスを止めない)。
#   - 止めるのは、このスクリプトが起動したプロセスだけ(Stop-Process -Id)。名前での強制終了はしない。
#   - 作る・変える・消すフォルダは、%TEMP%\kt_test_* の中だけ。
#   - キーを送る前に、入力欄の中身などが想定どおりか確認し、違えば中止する。
#   - 引数でコマンドを受け取らない(許可ルールを、スクリプトごとに固定の呼び出しにするため)。

$ErrorActionPreference = 'Stop'

$global:Kt = @{
    Exe  = Join-Path $env:TEMP 'kt_build_check\KT_Explorer.exe'
    Dll  = Join-Path $env:TEMP 'kt_build_check\KT_Explorer.dll'
    Ok   = 0
    Ng   = 0
    Proc = $null
    Win  = $null
}

# ───────── 結果の集計 ─────────

function Check([string]$name, $cond) {
    if ($cond) { $global:Kt.Ok++; Write-Output "OK  $name" } else { $global:Kt.Ng++; Write-Output "NG  $name" }
}

function Show-Result {
    Write-Output "RESULT: ok=$($global:Kt.Ok) ng=$($global:Kt.Ng)"
    if ($global:Kt.Ng -gt 0) { exit 1 }
}

function Stop-Test([string]$reason) {
    Write-Output "ABORT: $reason"
    Stop-KtApp
    exit 2
}

# ───────── テスト用フォルダ(%TEMP%\kt_test_* の中だけ) ─────────

function Test-UnderTestRoot([string]$path) {
    $full = [System.IO.Path]::GetFullPath($path)
    $root = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\kt_test_'
    return $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
}

function New-TestDir([string]$name) {
    if (-not $name.StartsWith('kt_test_')) { throw "test folder name must start with kt_test_: $name" }
    $d = Join-Path $env:TEMP $name
    Remove-TestDir $d
    New-Item -ItemType Directory $d | Out-Null
    return $d
}

function Remove-TestDir([string]$d) {
    if (-not (Test-UnderTestRoot $d)) { throw "refusing to delete outside %TEMP%\kt_test_*: $d" }
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force -Confirm:$false }
}

# ───────── アプリの起動・終了(画面テスト用) ─────────

function Initialize-Ui {
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
    if (-not ('KtWin' -as [type])) {
        Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KtWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint flags, UIntPtr extra);
}
"@
    }
    $global:Kt.A = [Windows.Automation.AutomationElement]
}

function Start-KtApp {
    Initialize-Ui
    if (Get-Process KT_Explorer -ErrorAction SilentlyContinue) {
        Write-Output 'ABORT: KT_Explorer is already running. Close it first (this script never stops a process it did not start).'
        exit 2
    }
    if (-not (Test-Path -LiteralPath $global:Kt.Exe)) {
        Write-Output "ABORT: build not found: $($global:Kt.Exe)  (run: dotnet build src/KT_Explorer/KT_Explorer.csproj -o `"`$TEMP/kt_build_check`")"
        exit 2
    }
    $A = $global:Kt.A
    $global:Kt.Proc = Start-Process $global:Kt.Exe -PassThru
    Start-Sleep -Seconds 4
    $global:Kt.Root = $A::RootElement
    $global:Kt.PidCond = New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $global:Kt.Proc.Id)
    $global:Kt.Win = $global:Kt.Root.FindFirst('Children', $global:Kt.PidCond)
    if (-not $global:Kt.Win) { Stop-Test 'app window not found' }
    Set-Foreground
}

function Stop-KtApp {
    # 自分が起動したプロセスだけを止める。終了時の状態保存は走らないので、ユーザーの設定は変わらない
    $p = $global:Kt.Proc
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

function Set-Foreground {
    [void][KtWin]::SetForegroundWindow([IntPtr]$global:Kt.Win.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 400
}

# ───────── 画面要素の取得・操作 ─────────

function Find-ById([string]$id) {
    $A = $global:Kt.A
    $global:Kt.Win.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)))
}

function Find-All([string]$controlType) {
    $A = $global:Kt.A
    $ct = [Windows.Automation.ControlType]::($controlType)
    @($global:Kt.Win.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, $ct))))
}

function Get-Text($el) {
    if ($el) { $el.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } else { '' }
}

# SendKeys の特殊文字をエスケープして、そのまま入力する
function Send-Text([string]$t) {
    $esc = [regex]::Replace($t, '[+^%~(){}\[\]]', { param($m) '{' + $m.Value + '}' })
    [System.Windows.Forms.SendKeys]::SendWait($esc)
}

function Send-Keys([string]$keys, [int]$wait = 600) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds $wait
}

function Get-PathText { Get-Text (Find-ById 'PathBox') }

# パス欄に入力して移動する。入力欄を書き換えるのではなく実際に打つので、表示の連動(OneWay バインド)は壊れない。
# 移動できたこと(パス欄の表示が目的のパスと一致)を確認し、できなければ中止する。
function Move-To([string]$path) {
    $pb = Find-ById 'PathBox'
    Set-Foreground
    $pb.SetFocus(); Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Send-Text $path
    Send-Keys '{ENTER}' 1500
    if ((Get-PathText) -ne $path) { Stop-Test "could not navigate to '$path' (path box shows '$(Get-PathText)')" }
}

function Get-SelectedTreeItem {
    Find-All 'TreeItem' | Where-Object {
        try { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false }
    } | Select-Object -First 1
}

# 名前の入力欄(PathBox / FilterBox / SearchBox 以外の Edit)
function Get-InlineEdit {
    Find-All 'Edit' | Where-Object { $_.Current.AutomationId -notin 'PathBox', 'FilterBox', 'SearchBox' } | Select-Object -First 1
}

function Find-AppMenuItem([string]$name) {
    $A = $global:Kt.A
    $global:Kt.Root.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name))) |
        Where-Object { $_.Current.ProcessId -eq $global:Kt.Proc.Id } | Select-Object -First 1
}

# 位置が取れた要素の中央を、実際のマウスでクリックする
function Click-Element($el) {
    $r = $el.Current.BoundingRectangle
    if ([double]::IsInfinity($r.X) -or [double]::IsNaN($r.X)) { Stop-Test "no screen position for '$($el.Current.Name)'" }
    Click-Point ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

function Click-Point([int]$x, [int]$y) {
    [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 400
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
}

# メニューキー(Apps)を押して、キーボードから右クリックメニューを開く
function Open-ContextMenuByKey {
    [KtWin]::keybd_event(0x5D, 0, 0, [UIntPtr]::Zero); [KtWin]::keybd_event(0x5D, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1000
}
