# KT_Explorer 画面テスト・ロジックテストの共通部品(各テストスクリプトから . で読み込んで使う)
#
# 安全のための決まり(変更するときも守ること):
#   - すでに KT_Explorer が起動していたら、閉じてからテストする(ユーザーの指示)。閉じるときは、まずウィンドウを閉じる
#     操作(状態の保存が走る)をしてから、残っていれば強制終了する。閉じてよいのは、名前が KT_Explorer のプロセスだけ。
#   - このスクリプトが起動したテスト用のアプリは、終了時に Stop-Process -Id で止める(状態は保存しない)。
#   - 作る・変える・消すフォルダは、%TEMP%\kt_test_* の中だけ。
#   - キーを送る前に、入力欄の中身などが想定どおりか確認し、違えば中止する。
#   - キーを送る前に、前面にあるのがテスト用の KT_Explorer であることを確かめ、違えば、何も送らずに中止する(ユーザーが別のアプリを使っているとき、そのアプリへ送らないため)。
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
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public static uint ForegroundPid() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
}
"@
    }
    $global:Kt.A = [Windows.Automation.AutomationElement]
}

# すでに起動している KT_Explorer を閉じる(ユーザーの指示: 起動していたら、強制的に閉じて実装・確認する)。
# 閉じるときに、タブの構成などの状態が保存されるよう、先にウィンドウを閉じる操作をしてから、残っていれば強制終了する。
# (トレイ常駐の設定では、閉じる操作は「隠す」になるが、そのときにも状態は保存される。)
function Close-RunningKtApp {
    $running = @(Get-Process KT_Explorer -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }
    foreach ($p in $running) {
        Write-Output "closing the running KT_Explorer (pid $($p.Id), started $($p.StartTime.ToString('HH:mm:ss')))"
        try { [void]$p.CloseMainWindow() } catch { }
    }
    [void](Wait-Until { -not (Get-Process KT_Explorer -ErrorAction SilentlyContinue) } 2500 100)   # 閉じる操作で終わるのを、最大 2.5 秒待つ
    foreach ($p in @(Get-Process KT_Explorer -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    [void](Wait-Until { -not (Get-Process KT_Explorer -ErrorAction SilentlyContinue) } 3000 100)
    if (Get-Process KT_Explorer -ErrorAction SilentlyContinue) {
        Write-Output 'ABORT: could not close the running KT_Explorer.'
        exit 2
    }
}

function Start-KtApp {
    Initialize-Ui
    Close-RunningKtApp
    if (-not (Test-Path -LiteralPath $global:Kt.Exe)) {
        Write-Output "ABORT: build not found: $($global:Kt.Exe)  (run: dotnet build src/KT_Explorer/KT_Explorer.csproj -o `"`$TEMP/kt_build_check`")"
        exit 2
    }
    $A = $global:Kt.A
    $global:Kt.Proc = Start-Process $global:Kt.Exe -PassThru
    $global:Kt.Root = $A::RootElement
    $global:Kt.PidCond = New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $global:Kt.Proc.Id)
    # ウィンドウが現れるまで待つ(最大 15 秒)。現れたら、中身の読み込みのために、少し置く
    [void](Wait-Until { $global:Kt.Win = $global:Kt.Root.FindFirst('Children', $global:Kt.PidCond); [bool]$global:Kt.Win } 15000 200)
    if (-not $global:Kt.Win) { Stop-Test 'app window not found' }
    Start-Sleep -Milliseconds 1500
    Set-Foreground
}

function Stop-KtApp {
    # 自分が起動したプロセスだけを止める。終了時の状態保存は走らないので、ユーザーの設定は変わらない
    $p = $global:Kt.Proc
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

# 条件が成り立つまで、短い間隔で確かめる(固定の待ちの代わり)。成り立てば $true、時間切れなら $false。
# 速いときは待たず、遅いときだけ、timeoutMs まで待つ。
function Wait-Until([scriptblock]$cond, [int]$timeoutMs = 3000, [int]$pollMs = 100) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if (& $cond) { return $true }
        if ($sw.ElapsedMilliseconds -ge $timeoutMs) { return $false }
        Start-Sleep -Milliseconds $pollMs
    }
}

function Set-Foreground {
    [void][KtWin]::SetForegroundWindow([IntPtr]$global:Kt.Win.Current.NativeWindowHandle)
    # 前面に出るまで待つ(最大 1 秒)。出たら、少しだけ落ち着かせる
    [void](Wait-Until { [KtWin]::ForegroundPid() -eq $global:Kt.Proc.Id } 1000 50)
    Start-Sleep -Milliseconds 120
}

# キーを送る前に、いま前面にあるのが、テスト用の KT_Explorer であることを確かめる。
# ユーザーが別のアプリ(ブラウザなど)を使っていて、前面を奪えないときに、そのアプリへキーを送ってしまわないため。
# 前面に出せなければ、何も送らずに中止する。
function Assert-AppForeground {
    for ($i = 0; $i -lt 3; $i++) {
        if ([KtWin]::ForegroundPid() -eq $global:Kt.Proc.Id) { return }
        Set-Foreground
    }
    Stop-Test 'the test app is not in the foreground (another app is being used). No keys were sent. Leave the keyboard and mouse alone while the UI tests run, and run it again.'
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
    Assert-AppForeground
    $esc = [regex]::Replace($t, '[+^%~(){}\[\]]', { param($m) '{' + $m.Value + '}' })
    [System.Windows.Forms.SendKeys]::SendWait($esc)
}

function Send-Keys([string]$keys, [int]$wait = 600) {
    Assert-AppForeground
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds $wait
}

function Get-PathText { Get-Text (Find-ById 'PathBox') }

# パス欄に入力して移動する。入力欄を書き換えるのではなく実際に打つので、表示の連動(OneWay バインド)は壊れない。
# 移動できたこと(パス欄の表示が目的のパスと一致)を確認し、できなければ中止する。
function Move-To([string]$path) {
    $pb = Find-ById 'PathBox'
    Set-Foreground
    $pb.SetFocus(); Start-Sleep -Milliseconds 150
    Assert-AppForeground
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Send-Text $path
    Send-Keys '{ENTER}' 0
    # パス欄の表示が、目的のパスになるまで待つ(最大 4 秒)。なったら、一覧・ツリーの読み込みのために、少し置く
    [void](Wait-Until { (Get-PathText) -eq $path } 4000 100)
    Start-Sleep -Milliseconds 500
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
    # デスクトップ全体を調べず、このアプリのトップレベルのウィンドウ(メイン + メニューなどのポップアップ)の中だけを調べる(他のアプリの分だけ、遅くなるため)
    $byName = New-Object Windows.Automation.PropertyCondition($A::NameProperty, $name)
    foreach ($w in $global:Kt.Root.FindAll('Children', $global:Kt.PidCond)) {
        $hit = $w.FindFirst('Descendants', $byName)
        if ($hit) { return $hit }
        if ($w.Current.Name -eq $name) { return $w }
    }
}

# 位置が取れた要素の中央を、実際のマウスでクリックする
function Click-Element($el) {
    $r = $el.Current.BoundingRectangle
    if ([double]::IsInfinity($r.X) -or [double]::IsNaN($r.X)) { Stop-Test "no screen position for '$($el.Current.Name)'" }
    Click-Point ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

function Click-Point([int]$x, [int]$y) {
    Assert-AppForeground   # 別のウィンドウが前面にあるとき、その上をクリックしてしまわないため
    [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 400
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
}

# メニューキー(Apps)を押して、キーボードから右クリックメニューを開く
function Open-ContextMenuByKey {
    Assert-AppForeground
    [KtWin]::keybd_event(0x5D, 0, 0, [UIntPtr]::Zero); [KtWin]::keybd_event(0x5D, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1000
}
