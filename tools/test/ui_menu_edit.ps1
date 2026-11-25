# メニューバー「編集」(元に戻す・やり直し・切り取り・コピー・貼り付け・削除・全て選択)の画面テスト。
# ファイルの操作(一覧にフォーカス)と、文字の操作(絞込欄にフォーカス)の両方を確かめる。
# 削除は「ごみ箱へ」。確認のダイアログが出る設定の PC では、出たダイアログを閉じて、中止する。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\ui_menu_edit.ps1'
# 操作するのは %TEMP%\kt_test_ui_menu_edit の中だけ。KT_Explorer が起動中なら、閉じてからテストする(ユーザーの指示)。
# 実行中は、キーボードとマウスに触らないこと(クリップボードの内容を書き換える)。
. (Join-Path $PSScriptRoot 'common.ps1')
Add-Type -AssemblyName System.Windows.Forms

$d = New-TestDir 'kt_test_ui_menu_edit'
New-Item -ItemType Directory (Join-Path $d 'dest') | Out-Null
Set-Content -LiteralPath (Join-Path $d 'dest\keep.txt') -Value 'keep'   # 空でないフォルダにする(一覧にフォーカスを移すため)
foreach ($n in 'a.txt', 'b.txt', 'c.txt') { Set-Content -LiteralPath (Join-Path $d $n) -Value $n }

function Open-EditMenu {
    Set-Foreground
    $m = Find-AppMenuItem '編集(E)'
    if (-not $m) { Stop-Test 'Edit menu not found' }
    $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    # メニューの項目が現れるまで待つ(固定の待ちではなく)
    [void](Wait-Until { [bool](Find-AppMenuItem '全て選択(A)') } 3000 100)
}
function Edit-Item([string]$name) { Find-AppMenuItem $name }
# メニューを閉じる: 1 回目の Esc で項目が消えるまで待ち、2 回目の Esc で、メニューバーからフォーカスを戻す
function Close-EditMenu {
    Send-Keys '{ESC}' 0
    [void](Wait-Until { -not (Find-AppMenuItem '全て選択(A)') } 2000 100)
    Send-Keys '{ESC}' 150
}
function Pick-Edit([string]$name) {
    Open-EditMenu
    $i = Edit-Item $name
    if (-not $i) { Stop-Test "edit menu item '$name' not found" }
    if (-not $i.Current.IsEnabled) { Close-EditMenu; return $false }
    $i.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250
    $true   # 結果は、呼び出し側が、期待する状態になるまで待って確かめる(Wait-Until)
}
function Edit-Enabled([string]$name) { Open-EditMenu; $r = (Edit-Item $name).Current.IsEnabled; Close-EditMenu; $r }
function Get-Rows {
    $r = @(Find-All 'DataItem'); if ($r.Count -eq 0) { $r = @(Find-All 'ListItem' | Where-Object { $_.Current.BoundingRectangle.Y -gt ($global:Kt.Win.Current.BoundingRectangle.Y + 125) }) }; $r
}
function Row-Names { @(Get-Rows | ForEach-Object { $_.Current.Name }) }
# 名前が一致する行を選択する(行の名前はファイル名ではなく型名のことがあるので、中の文字で探す)
function Select-File([string]$name) {
    $A = $global:Kt.A
    foreach ($row in Get-Rows) {
        $t = $row.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
        if ($t -and $t.Current.Name -eq $name) {
            $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); $row.SetFocus(); Start-Sleep -Milliseconds 200
            return $true
        }
    }
    $false
}
function Selected-Count { @(Get-Rows | Where-Object { try { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { $false } }).Count }
# 一覧の項目を選んで、一覧にフォーカスを移す(パス欄に入力したあとは、フォーカスがパス欄に残り、編集メニューは文字に働くため)
function Focus-List([string]$name) { if (-not (Select-File $name)) { Stop-Test "could not focus the list (file '$name' not found)" } }
function Dir-Has([string]$dir, [string]$name) { Test-Path -LiteralPath (Join-Path $dir $name) }

try {
    Start-KtApp
    Move-To $d

    # ── 1) 何も選んでいないとき: 切り取り・コピー・削除は無効、全て選択は有効
    Check '1) with nothing selected: cut / copy / delete are disabled' ((-not (Edit-Enabled '切り取り(T)')) -and (-not (Edit-Enabled 'コピー(C)')) -and (-not (Edit-Enabled '削除(D)')))
    Check '1) select-all is enabled' (Edit-Enabled '全て選択(A)')

    # ── 2) 全て選択(一覧にフォーカスがあるとき)
    Focus-List 'a.txt'
    [void](Pick-Edit '全て選択(A)')
    [void](Wait-Until { (Selected-Count) -eq 4 } 3000 150)
    Check "2) select-all selects every item (selected: $(Selected-Count))" ((Selected-Count) -eq 4)

    # ── 3) コピー → 別のフォルダで貼り付け
    [void](Select-File 'a.txt')
    Check '3) with a file selected: cut / copy / delete are enabled' ((Edit-Enabled 'コピー(C)') -and (Edit-Enabled '切り取り(T)') -and (Edit-Enabled '削除(D)'))
    [void](Pick-Edit 'コピー(C)')
    Move-To (Join-Path $d 'dest'); Focus-List 'keep.txt'
    Check '3) paste is enabled in another folder' (Edit-Enabled '貼り付け(P)')
    [void](Pick-Edit '貼り付け(P)')
    [void](Wait-Until { Dir-Has "$d\dest" 'a.txt' } 5000 100)
    Check '3) copy + paste: a.txt is now in dest, and still in the original folder' ((Dir-Has "$d\dest" 'a.txt') -and (Dir-Has $d 'a.txt'))

    # ── 4) 元に戻す(貼り付けの取り消し)・やり直し(一覧にフォーカスがあるとき)
    Focus-List 'keep.txt'
    Check '4) undo is enabled after the paste' (Edit-Enabled '元に戻す(U)')
    [void](Pick-Edit '元に戻す(U)')
    [void](Wait-Until { -not (Dir-Has "$d\dest" 'a.txt') } 5000 100)
    Check '4) undo removes the pasted copy' (-not (Dir-Has "$d\dest" 'a.txt'))
    Check '4) redo is enabled after the undo' (Edit-Enabled 'やり直し(R)')
    [void](Pick-Edit 'やり直し(R)')
    [void](Wait-Until { Dir-Has "$d\dest" 'a.txt' } 5000 100)
    Check '4) redo brings the copy back' (Dir-Has "$d\dest" 'a.txt')

    # ── 5) 切り取り → 貼り付け(移動)
    Move-To $d
    [void](Select-File 'b.txt')
    [void](Pick-Edit '切り取り(T)')
    Move-To (Join-Path $d 'dest'); Focus-List 'keep.txt'
    [void](Pick-Edit '貼り付け(P)')
    [void](Wait-Until { (Dir-Has "$d\dest" 'b.txt') -and (-not (Dir-Has $d 'b.txt')) } 5000 100)
    Check '5) cut + paste: b.txt moved into dest (gone from the original folder)' ((Dir-Has "$d\dest" 'b.txt') -and (-not (Dir-Has $d 'b.txt')))

    # ── 6) 削除(ごみ箱へ)
    Move-To $d
    [void](Select-File 'c.txt')
    [void](Pick-Edit '削除(D)')
    [void](Wait-Until { -not (Dir-Has $d 'c.txt') } 5000 100)
    $dlg = $global:Kt.Root.FindFirst('Children', (New-Object Windows.Automation.AndCondition($global:Kt.PidCond, (New-Object Windows.Automation.PropertyCondition($global:Kt.A::ControlTypeProperty, [Windows.Automation.ControlType]::Window)))))
    Check '6) delete moves c.txt to the recycle bin (gone from the folder)' (-not (Dir-Has $d 'c.txt'))

    # ── 7) 文字の入力欄にフォーカスがあるとき: 編集メニューは、ファイルではなく文字に働く
    $filter = Find-ById 'FilterBox'
    Set-Foreground; $filter.SetFocus(); Start-Sleep -Milliseconds 200
    Send-Text 'hello'; Start-Sleep -Milliseconds 150
    Send-Keys '^a' 150
    Check '7) in the filter box with text selected: copy / cut are enabled' ((Edit-Enabled 'コピー(C)') -and (Edit-Enabled '切り取り(T)'))
    # メニューを開閉してもフォーカスは入力欄に残る。コピー → クリップボードは、ファイルではなく文字
    $filter.SetFocus(); Start-Sleep -Milliseconds 150; Send-Keys '^a' 150
    [void](Pick-Edit 'コピー(C)')
    [void](Wait-Until { [System.Windows.Forms.Clipboard]::GetText() -eq 'hello' } 3000 100)
    $clip = [System.Windows.Forms.Clipboard]::GetText()
    Check "7) copy in a text box puts the text on the clipboard (clipboard: '$clip')" ($clip -eq 'hello')
    # 切り取り → 欄が空になる。貼り付け → 戻る
    $filter.SetFocus(); Start-Sleep -Milliseconds 150; Send-Keys '^a' 150
    [void](Pick-Edit '切り取り(T)')
    [void](Wait-Until { (Get-Text $filter) -eq '' } 3000 100)
    Check "7) cut in a text box empties it (text: '$(Get-Text $filter)')" ((Get-Text $filter) -eq '')
    $filter.SetFocus(); Start-Sleep -Milliseconds 150
    [void](Pick-Edit '貼り付け(P)')
    [void](Wait-Until { (Get-Text $filter) -eq 'hello' } 3000 100)
    Check "7) paste in a text box puts the text back (text: '$(Get-Text $filter)')" ((Get-Text $filter) -eq 'hello')
    Send-Keys '^a' 150; $filter.SetFocus(); Send-Keys '^a' 150
    [void](Pick-Edit '全て選択(A)')
    [void](Pick-Edit '削除(D)')
    [void](Wait-Until { (Get-Text $filter) -eq '' } 3000 100)
    Check "7) select-all + delete in a text box clears it (text: '$(Get-Text $filter)')" ((Get-Text $filter) -eq '')
}
finally {
    Stop-KtApp
    Remove-TestDir $d
}
Show-Result
