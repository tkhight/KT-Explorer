# マウスジェスチャ「右ボタン+ホイール」の画面テスト(実際のマウス入力): 上=左のタブ・下=右のタブ、Ctrl つきは前・次のタブグループ。
# 注意: StrokesPlus などの、マウスジェスチャの常駐ツールが有効だと、合成した右ボタンの入力が、アプリに届かず、失敗する(ツールを無効にして実行する)。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_gesture_wheel.ps1'
# 操作するのは %TEMP%\kt_test_ui_gesture_wheel の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 変更はアプリの中だけ(終了時の保存は走らない)。実行中は、キーボードとマウスに触らないこと。
. (Join-Path $PSScriptRoot 'common.ps1')

$d = New-TestDir 'kt_test_ui_gesture_wheel'
New-Item -ItemType Directory (Join-Path $d 'one'), (Join-Path $d 'two') | Out-Null
1..40 | ForEach-Object { Set-Content -LiteralPath (Join-Path $d ('one\f{0:00}.txt' -f $_)) -Value 'x' }

# 右ボタンを押したまま(Ctrl を押していれば Ctrl も)ホイールを回して、離す。位置は、ウィンドウの中ほど(一覧の上)
function Wheel-WithRight([int]$delta, [switch]$Ctrl) {
    Assert-AppForeground
    $r = $global:Kt.Win.Current.BoundingRectangle
    [void][KtWin]::SetCursorPos([int]($r.X + $r.Width * 0.6), [int]($r.Y + $r.Height * 0.6)); Start-Sleep -Milliseconds 500
    if ($Ctrl) { [KtWin]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200 }
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
    $data = if ($delta -gt 0) { [uint32]$delta } else { [uint32](4294967296 - [math]::Abs($delta)) }
    [KtWin]::mouse_event(0x800, 0, 0, $data, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700
    [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
    if ($Ctrl) { [KtWin]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 900
}

# タブ・グループの一覧(画面要素の名前は、項目の型名になる)と、選択中の位置
function Selected-Position([string]$typeName) {
    $items = @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like "*$typeName*" })
    for ($i = 0; $i -lt $items.Count; $i++) {
        try { if ($items[$i].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return "$($i + 1)/$($items.Count)" } } catch { }
    }
    "none/$($items.Count)"
}

try {
    Start-KtApp
    Move-To "$d\one"

    # ── 準備: タブを 2 つ足す(新しいタブは、作ると選択される → 最後のタブ)。タブの数は、保存されていた状態で変わるので、位置は数に対して比べる
    Set-Foreground; Send-Keys '^t' 700; Send-Keys '^t' 700
    $pos = (Selected-Position 'FolderTab') -split '/'
    $n = [int]$pos[1]
    Check "0) the newest tab is selected, and there are at least 3 tabs ($(Selected-Position 'FolderTab'))" (([int]$pos[0] -eq $n) -and ($n -ge 3))

    # ── 1) 右ボタン+ホイール上 = 左のタブ
    Wheel-WithRight 120
    Check "1) right button + wheel up selects the tab on the left ($(Selected-Position 'FolderTab'))" ((Selected-Position 'FolderTab') -eq "$($n - 1)/$n")
    Check '2) the context menu did not open after releasing the button' (-not (Find-AppMenuItem '貼り付け'))
    # ── 3) 下 = 右のタブ
    Wheel-WithRight -120
    Check "3) right button + wheel down selects the tab on the right ($(Selected-Position 'FolderTab'))" ((Selected-Position 'FolderTab') -eq "$n/$n")

    # ── 準備: タブグループを追加(作ると選択される → 2/2)
    Set-Foreground
    $add = Find-ById 'AddGroupButton'
    if (-not $add) { Stop-Test 'the add-group button was not found' }
    $add.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200
    Send-Text 'G2'; Send-Keys '{ENTER}' 1000
    Check "4) a second group was added and selected ($(Selected-Position 'TabGroup'))" ((Selected-Position 'TabGroup') -eq '2/2')

    # ── 5) Ctrl + 右ボタン + ホイール上 = 前のタブグループ(デフォルト)
    Wheel-WithRight 120 -Ctrl
    Check "5) Ctrl + right button + wheel up selects the previous group ($(Selected-Position 'TabGroup'))" ((Selected-Position 'TabGroup') -eq '1/2')
    # ── 6) 下 = 次のタブグループ(G2)
    Wheel-WithRight -120 -Ctrl
    Check "6) Ctrl + right button + wheel down selects the next group ($(Selected-Position 'TabGroup'))" ((Selected-Position 'TabGroup') -eq '2/2')
    # ── 7) もう一度下: 端から反対の端へ回り込む
    Wheel-WithRight -120 -Ctrl
    Check "7) wheel down at the last group wraps to the first ($(Selected-Position 'TabGroup'))" ((Selected-Position 'TabGroup') -eq '1/2')

    # ── 8) 右ボタンを押さないホイールは、何も切り替えない
    Assert-AppForeground
    $r = $global:Kt.Win.Current.BoundingRectangle
    [void][KtWin]::SetCursorPos([int]($r.X + $r.Width * 0.6), [int]($r.Y + $r.Height * 0.6)); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(0x800, 0, 0, 120, [UIntPtr]::Zero); Start-Sleep -Milliseconds 800
    Check "8) wheel alone does not switch the group ($(Selected-Position 'TabGroup'))" ((Selected-Position 'TabGroup') -eq '1/2')

    # ── 9) 普通の右クリック(動かさない)は、メニューが出る
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200
    Check '9) a plain right click still opens the context menu' ([bool](Find-AppMenuItem '貼り付け'))
    Send-Keys '{ESC}' 500

    # ── 10) ロッカー: 右ボタンを押したまま左クリック = いまのタブを閉じる。右ボタンを離しても、メニューは出ない
    function Tab-Count { @(Find-All 'ListItem' | Where-Object { $_.Current.Name -like '*FolderTab*' }).Count }
    $before = Tab-Count
    Assert-AppForeground
    [void][KtWin]::SetCursorPos([int]($r.X + $r.Width * 0.6), [int]($r.Y + $r.Height * 0.6)); Start-Sleep -Milliseconds 500
    [KtWin]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300       # 右ボタンを押す
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700   # 左クリック
    [KtWin]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200     # 右ボタンを離す
    Check "10) right button held + left click closes the current tab ($before -> $(Tab-Count))" ((Tab-Count) -eq $before - 1)
    Check '10) the context menu did not open after releasing the right button' (-not (Find-AppMenuItem '貼り付け'))

    # ── 11) 右ボタンを押さない左クリックは、タブを閉じない
    $before = Tab-Count
    [KtWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [KtWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 900
    Check "11) a plain left click does not close a tab ($before -> $(Tab-Count))" ((Tab-Count) -eq $before)
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
