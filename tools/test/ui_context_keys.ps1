# 右クリックメニューの 1 文字キーの画面テスト: 一覧の項目(D=削除、M=名前の変更、C=コピー、P=貼り付け)、一覧の空白(W→F=新規フォルダー)、
# ツリー(M=名前の変更)、日本語入力がオンのままでも効くこと。D で消すファイル(テスト用の 1 つ)は、ごみ箱に入る。クリップボードを書き換える。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_context_keys.ps1'
# 操作するのは %TEMP%\kt_test_ui_context_keys の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。マウスは実際に動かす。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_context_keys'
New-Item -ItemType Directory (Join-Path $d 'sub') | Out-Null
foreach ($n in 'a.txt', 'b.txt', 'del.txt') { Set-Content -LiteralPath (Join-Path $d $n) -Value 'x' }

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KtIme {
  [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  public static int GetOpen(IntPtr h) { return (int)SendMessage(ImmGetDefaultIMEWnd(h), 0x283, (IntPtr)5, IntPtr.Zero); }
  public static void SetOpen(IntPtr h, int open) { SendMessage(ImmGetDefaultIMEWnd(h), 0x283, (IntPtr)6, (IntPtr)open); }
}
"@

function Press([byte]$vk) {
    Assert-AppForeground
    [KtWin]::keybd_event($vk, 0, 0, [UIntPtr]::Zero); [KtWin]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 900
}
function Press-Letter([char]$c) { Press ([byte][char]::ToUpper($c)) }
# 一覧の中の、その名前の文字
function Name-Text([string]$name) {
    $tree = $global:Kt.Win.Current.BoundingRectangle.X + 250   # ツリーより右
    Find-All 'Text' | Where-Object { $_.Current.Name -eq $name -and $_.Current.BoundingRectangle.X -gt $tree -and $_.Current.BoundingRectangle.Y -gt ($global:Kt.Win.Current.BoundingRectangle.Y + 150) } | Select-Object -First 1
}
function Right-Click([int]$x, [int]$y) {
    Assert-AppForeground
    [void][KtWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
    [void](Wait-Until { [bool](Find-AppMenuItem 'プロパティ(R)') } 4000 150)
    Start-Sleep -Milliseconds 300
}
function Right-Click-Name([string]$name) {
    $t = Name-Text $name
    if (-not $t) { Stop-Test "'$name' is not in the list" }
    $r = $t.Current.BoundingRectangle
    Right-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

try {
    Start-KtApp
    Move-To $d

    # ── 1) 項目の右クリック: 見出しにキーが付いている
    Right-Click-Name 'a.txt'
    $names = '開く(O)', '切り取り(T)', 'コピー(C)', '貼り付け(P)', '名前の変更(M)', '削除(D)', 'パスをコピー(A)', 'プロパティ(R)', 'その他のオプション(X)'
    $missing = @($names | Where-Object { -not (Find-AppMenuItem $_) })
    Check "1) the item menu shows the keys (missing: $($missing -join ','))" ($missing.Count -eq 0)
    Press 0x1B

    # ── 2) 右クリック → D: 削除
    Right-Click-Name 'del.txt'
    Press-Letter 'D'
    [void](Wait-Until { -not (Test-Path -LiteralPath (Join-Path $d 'del.txt')) } 4000 150)
    Check '2) right-click then D deletes the item' (-not (Test-Path -LiteralPath (Join-Path $d 'del.txt')))

    # ── 3) C でコピー → フォルダの右クリック → P で、その中へ貼り付け
    Right-Click-Name 'a.txt'
    Press-Letter 'C'
    Right-Click-Name 'sub'
    Press-Letter 'P'
    [void](Wait-Until { Test-Path -LiteralPath (Join-Path $d 'sub\a.txt') } 4000 150)
    Check '3) C copies, and P on a folder pastes into it' (Test-Path -LiteralPath (Join-Path $d 'sub\a.txt'))

    # ── 4) 日本語入力がオンのままでも、キーが効く(M = 名前の変更)
    $h = [IntPtr]$global:Kt.Win.Current.NativeWindowHandle
    $before = [KtIme]::GetOpen($h)
    Right-Click-Name 'b.txt'
    Press 0x1B
    [KtIme]::SetOpen($h, 1); Start-Sleep -Milliseconds 400
    $imeOn = [KtIme]::GetOpen($h) -ne 0
    Right-Click-Name 'b.txt'
    Press-Letter 'M'
    $e = Get-InlineEdit
    Check "4) M starts renaming (Japanese IME on: $imeOn)" ($e -and (Get-Text $e) -eq 'b.txt')
    Press 0x1B; Press 0x1B   # IME がオンのとき、1 回目の Esc が IME に取られても、入力欄を閉じる
    [KtIme]::SetOpen($h, $before); Start-Sleep -Milliseconds 400

    # ── 5) 一覧の空白の右クリック → W → F: 新しいフォルダー
    $r = (Name-Text 'b.txt').Current.BoundingRectangle
    Right-Click ([int]($r.X + 100)) ([int]($r.Y + 160))
    Check '5) the blank menu shows 新規作成(W) and 更新(E)' ([bool](Find-AppMenuItem '新規作成(W)') -and [bool](Find-AppMenuItem '更新(E)'))
    Press-Letter 'W'
    Press-Letter 'F'
    [void](Wait-Until { Test-Path -LiteralPath (Join-Path $d '新しいフォルダー') } 4000 150)
    Check '5) W then F creates a new folder' (Test-Path -LiteralPath (Join-Path $d '新しいフォルダー'))
    Send-Keys '{ESC}' 800

    # ── 6) ツリー: メニューキー → M で、名前の変更
    Move-To $d
    $sel = Get-SelectedTreeItem
    if (-not $sel) { Stop-Test 'no selected tree item' }
    $sel.SetFocus(); Start-Sleep -Milliseconds 400
    Open-ContextMenuByKey
    Check '6) the tree menu shows the keys' ([bool](Find-AppMenuItem '削除(D)') -and [bool](Find-AppMenuItem 'お気に入りに追加(F)'))
    Press-Letter 'M'
    $e = Get-InlineEdit
    Check '6) M in the tree menu starts renaming the folder' ($e -and (Get-Text $e) -eq 'kt_test_ui_context_keys')
    Press 0x1B
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
