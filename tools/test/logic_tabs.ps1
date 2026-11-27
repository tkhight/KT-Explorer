# タブ・ツリーのロジックの検証(画面は出さず、ビルドした DLL を直接呼ぶ)。
# 閉じたタブの開き直し / タブの複製 / フォルダの名前変更・削除への追従 / ツリーの更新 / 元に戻すとの整合。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_tabs.ps1'
# 操作するフォルダは %TEMP%\kt_test_logic の中だけ。ごみ箱も確認ダイアログも使わない(.NET で直接消す)。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) {
    Write-Output "ABORT: build not found: $($global:Kt.Dll)"
    exit 2
}
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$MVM = $asm.GetType('KT_Explorer.ViewModels.MainViewModel')
$AS  = $asm.GetType('KT_Explorer.Services.AppState')
$FOS = $asm.GetType('KT_Explorer.Services.FileOperationService')

$d = New-TestDir 'kt_test_logic'
try {
    foreach ($n in 'x\y', 'a', 'b', 'del\inner', 'sub') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
    $vm = [Activator]::CreateInstance($MVM, [object[]]@([Activator]::CreateInstance($AS)))
    function Tabs { @($vm.SelectedGroup.Tabs | ForEach-Object { $_.Path }) }
    $initial = (Tabs).Count

    # ── 閉じたタブの開き直し
    $vm.OpenInNewTab("$d\a"); $vm.OpenInNewTab("$d\b")
    Check 'tabs added' ((Tabs).Count -eq $initial + 2)
    Check 'nothing to reopen yet' (-not $vm.CanReopenClosedTab)
    $vm.CloseTabCommand.Execute($null)
    Check 'closed the selected tab (b)' (-not ((Tabs) -contains "$d\b"))
    Check 'can reopen' $vm.CanReopenClosedTab
    $vm.ReopenClosedTabCommand.Execute($null)
    Check 'reopened b at the end and selected' (((Tabs)[-1] -eq "$d\b") -and ($vm.SelectedTab.Path -eq "$d\b"))
    Check 'history consumed' (-not $vm.CanReopenClosedTab)
    $tabA = $vm.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\a" } | Select-Object -First 1
    $vm.CloseTabCommand.Execute($tabA); Remove-Item -LiteralPath "$d\a" -Recurse -Force -Confirm:$false
    $vm.ReopenClosedTabCommand.Execute($null)
    Check 'reopen skips a folder that no longer exists' (-not ((Tabs) -contains "$d\a"))

    # ── タブの複製
    $tabB = $vm.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\b" } | Select-Object -First 1
    $n = (Tabs).Count; $idx = $vm.SelectedGroup.Tabs.IndexOf($tabB)
    $vm.DuplicateTab($tabB)
    Check 'duplicate: a new tab right after, selected' (((Tabs).Count -eq $n + 1) -and ($vm.SelectedGroup.Tabs[$idx + 1].Path -eq "$d\b") -and ($vm.SelectedTab -eq $vm.SelectedGroup.Tabs[$idx + 1]))

    # ── 名前の変更への追従(タブが親フォルダの下を表示中でも、監視を止めてから変更する)
    $vm.OpenInNewTab("$d\x\y")
    $vm.PauseWatching("$d\x")
    $newPath = $FOS.GetMethod('Rename').Invoke($null, [object[]]@("$d\x", 'x2', $true))
    $vm.OnFolderRenamed("$d\x", $newPath); $vm.ResumeAllWatching()
    Check 'rename of the parent of an open tab succeeds' ($newPath -eq "$d\x2")
    Check 'tab follows to the new path' ((Tabs) -contains "$d\x2\y")
    Check 'no tab left at the old path' (-not ((Tabs) -contains "$d\x\y"))

    # ── 元に戻す(名前の変更の取り消し)のあと、無くなったフォルダのタブは親へ移る
    $err = $vm.Undo([IntPtr]::Zero)
    Check 'undo rename succeeds with an open tab inside' (($null -eq $err) -and (Test-Path -LiteralPath "$d\x\y") -and -not (Test-Path -LiteralPath "$d\x2"))
    Check 'after undo the tab follows back to the original name' ((Tabs) -contains "$d\x\y")
    Check 'after undo no tab is left at the new name' (-not ((Tabs) -contains "$d\x2\y"))
    $err = $vm.Redo([IntPtr]::Zero)
    Check 'redo rename succeeds' (($null -eq $err) -and (Test-Path -LiteralPath "$d\x2\y"))
    Check 'after redo the tab follows to the new name again' (((Tabs) -contains "$d\x2\y") -and -not ((Tabs) -contains "$d\x\y"))
    $err = $vm.Undo([IntPtr]::Zero)
    Check 'undo again (back to the original name) leaves the tab at the original name' (($null -eq $err) -and ((Tabs) -contains "$d\x\y"))

    # ── 移動の取り消しでも、移動した先を表示していたタブが、元の場所へ追従する
    New-Item -ItemType Directory "$d\mv_src\item\deep" | Out-Null; New-Item -ItemType Directory "$d\mv_dst" | Out-Null
    $vm.OpenInNewTab("$d\mv_src\item\deep")
    $vm.PauseWatching("$d\mv_src")
    $res = $FOS.GetMethod('Transfer').Invoke($null, [object[]]@([System.Collections.Generic.List[string]]@("$d\mv_src\item"), "$d\mv_dst", $true, [IntPtr]::Zero, $true, $null))
    $vm.OnFolderRenamed("$d\mv_src\item", "$d\mv_dst\item"); $vm.ResumeAllWatching()
    Check 'move: the tab inside the moved folder follows to the new location' ((Tabs) -contains "$d\mv_dst\item\deep")
    $err = $vm.Undo([IntPtr]::Zero)
    Check 'undo move: the tab follows back to the original location' (($null -eq $err) -and ((Tabs) -contains "$d\mv_src\item\deep"))

    # ── 削除への追従(ごみ箱を使わず完全削除)
    $vm.OpenInNewTab("$d\del\inner")
    $vm.PauseWatching("$d\del")
    # 削除そのもの(ごみ箱・確認ダイアログを伴う)ではなく、削除後の追従の処理を検証するので、.NET で直接消す
    [System.IO.Directory]::Delete("$d\del", $true)
    $vm.AfterFileChange()
    Check 'delete of an open folder' (-not (Test-Path -LiteralPath "$d\del"))
    Check 'tab of the deleted folder moved to the nearest existing parent' (((Tabs) -contains $d) -and -not ((Tabs) -contains "$d\del\inner"))

    # ── ツリーの更新(展開状態は保たれる)
    $root = $vm.TreeRoots | Where-Object { $d.StartsWith($_.FullPath.TrimEnd('\') + '\', 'OrdinalIgnoreCase') } |
        Sort-Object { $_.FullPath.Length } -Descending | Select-Object -First 1
    $node = $root.ExpandTo($d); $node.IsExpanded = $true
    $sub = $node.Children | Where-Object { $_.Name -eq 'sub' }; $sub.IsExpanded = $true
    New-Item -ItemType Directory "$d\newdir" | Out-Null; $vm.RefreshTree()
    Check 'tree adds a created folder' (@($node.Children | Where-Object { $_.Name -eq 'newdir' }).Count -eq 1)
    Check 'existing child kept (same instance, still expanded)' ((@($node.Children | Where-Object { $_.Name -eq 'sub' })[0] -eq $sub) -and $sub.IsExpanded)
    Rename-Item -LiteralPath "$d\newdir" -NewName 'renamed'; $vm.RefreshTree()
    Check 'tree reflects a rename' ((@($node.Children | Where-Object { $_.Name -eq 'renamed' }).Count -eq 1) -and (@($node.Children | Where-Object { $_.Name -eq 'newdir' }).Count -eq 0))
    Rename-Item -LiteralPath "$d\renamed" -NewName 'RENAMED'; $vm.RefreshTree()
    Check 'tree reflects a case-only rename' (@($node.Children | Where-Object { $_.Name -ceq 'RENAMED' }).Count -eq 1)
    Remove-Item -LiteralPath "$d\RENAMED" -Recurse -Force -Confirm:$false; $vm.RefreshTree()
    Check 'tree removes a deleted folder' (@($node.Children | Where-Object { $_.Name -like 'renamed' }).Count -eq 0)

    # ── フォルダのサイズ計算(100 + 200 + 300 = 600 バイト。空のフォルダは 0)
    $FSS = $asm.GetType('KT_Explorer.Services.FolderSizeService')
    New-Item -ItemType Directory "$d\sz\inner\deeper", "$d\sz_empty" | Out-Null
    [System.IO.File]::WriteAllBytes("$d\sz\a.bin", (New-Object byte[] 100))
    [System.IO.File]::WriteAllBytes("$d\sz\inner\b.bin", (New-Object byte[] 200))
    [System.IO.File]::WriteAllBytes("$d\sz\inner\deeper\c.bin", (New-Object byte[] 300))
    $none = [System.Threading.CancellationToken]::None
    Check 'folder size: sum of all files below' ($FSS.GetMethod('Calculate').Invoke($null, [object[]]@("$d\sz", $none)) -eq 600)
    Check 'folder size: empty folder is 0' ($FSS.GetMethod('Calculate').Invoke($null, [object[]]@("$d\sz_empty", $none)) -eq 0)
    $cts = New-Object System.Threading.CancellationTokenSource; $cts.Cancel()
    $cancelled = $false
    try { [void]$FSS.GetMethod('Calculate').Invoke($null, [object[]]@("$d\sz", $cts.Token)) } catch { $cancelled = $_.Exception.InnerException -is [System.OperationCanceledException] }
    Check 'folder size: a cancelled calculation stops' $cancelled

    # タブからの計算(一覧の項目 1 つ分)。一覧そのもの(画面のスレッドが要る)は、画面テストで確認する
    $FE = $asm.GetType('KT_Explorer.Models.FileEntry')
    $FT = $asm.GetType('KT_Explorer.ViewModels.FolderTabViewModel')
    $tab = [Activator]::CreateInstance($FT, [object[]]@([string]$d))
    $szEntry = [System.Activator]::CreateInstance($FE, [object[]]@([System.IO.DirectoryInfo]::new("$d\sz"), $null))
    Check 'folder size: not calculated until requested' ($szEntry.IsDirectory -and $null -eq $szEntry.Size -and $szEntry.SizeText -eq '')
    $arr = [Array]::CreateInstance($FE, 1); $arr.SetValue($szEntry, 0)
    $tab.CalculateFolderSizes($arr)
    for ($i = 0; $i -lt 100 -and ($null -eq $szEntry.Size -or $szEntry.IsCalculating); $i++) { Start-Sleep -Milliseconds 100 }
    Check 'folder size: calculated through the tab (600 B)' ($szEntry.Size -eq 600 -and $szEntry.SizeText -eq '600 B')
    Check 'folder size: not calculating any more' (-not $szEntry.IsCalculating)
    $tab.Dispose()

    # ── 並べ替えはタブごと。状態への保存と、復元
    $SC = $asm.GetType('KT_Explorer.Services.SortColumn')
    New-Item -ItemType Directory "$d\s1", "$d\s2" | Out-Null
    $vm.OpenInNewTab("$d\s1"); $vm.OpenInNewTab("$d\s2")
    $tS1 = $vm.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\s1" } | Select-Object -First 1
    $tS2 = $vm.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\s2" } | Select-Object -First 1
    $tS1.ToggleSort([Enum]::Parse($SC, 'Size'))
    Check 'per-tab sort: clicking a header changes only that tab' (("$($tS1.SortColumn)" -eq 'Size') -and ("$($tS2.SortColumn)" -eq 'Name'))
    $tS1.ToggleSort([Enum]::Parse($SC, 'Size'))
    Check 'per-tab sort: clicking the same header again reverses it' ($tS1.SortDescending -and -not $tS2.SortDescending)
    $tS3 = $vm.OpenInNewTab("$d\sub"); $tNew = $vm.SelectedTab
    Check 'per-tab sort: a new tab starts with the last used sort (size, descending)' (("$($tNew.SortColumn)" -eq 'Size') -and $tNew.SortDescending)
    $st = $vm.ToState()
    $g0 = $st.Groups[0]
    $idx = [array]::IndexOf(@($g0.TabPaths), "$d\s1"); $idx2 = [array]::IndexOf(@($g0.TabPaths), "$d\s2")
    Check 'state: each tab saves its own sort (same order as the paths)' (($idx -ge 0) -and ("$($g0.TabSorts[$idx].Column)" -eq 'Size') -and $g0.TabSorts[$idx].Descending -and ("$($g0.TabSorts[$idx2].Column)" -eq 'Name'))
    $vm2 = [Activator]::CreateInstance($MVM, [object[]]@($st))
    $r1 = $vm2.Groups[0].Tabs | Where-Object { $_.Path -eq "$d\s1" } | Select-Object -First 1
    $r2 = $vm2.Groups[0].Tabs | Where-Object { $_.Path -eq "$d\s2" } | Select-Object -First 1
    Check 'state: sorts are restored per tab' (($null -ne $r1) -and ("$($r1.SortColumn)" -eq 'Size') -and $r1.SortDescending -and ("$($r2.SortColumn)" -eq 'Name') -and -not $r2.SortDescending)

    # ── タブをグループへ移動 → 移動先のグループを表示して、移したタブをアクティブにする
    $g1 = $vm.SelectedGroup
    $vm.AddGroup('G2'); $g2 = $vm.SelectedGroup
    $vm.SelectedGroup = $g1
    $vm.OpenInNewTab("$d2") 2>$null
    New-Item -ItemType Directory "$d\mvg" -Force | Out-Null
    $vm.OpenInNewTab("$d\mvg"); $mv = $vm.SelectedTab
    $vm.OpenInNewTab("$d\sub"); $other = $vm.SelectedTab     # 別のタブが選択されている状態から、mvg のタブを移す
    $vm.MoveTabToGroup($mv, $g2)
    Check 'move to group: the destination group is shown' ($vm.SelectedGroup -eq $g2)
    Check 'move to group: the moved tab is in the destination and is the active tab' (($g2.Tabs -contains $mv) -and ($vm.SelectedTab -eq $mv) -and ($vm.CurrentFolder -eq $mv))
    Check 'move to group: the tab is gone from the source group' (-not ($g1.Tabs -contains $mv))
    $vm.SelectedGroup = $g1
    Check 'move to group: going back to the source group restores the tab that was active there' ($vm.SelectedTab -eq $other)
    # 表示中のタブを移した場合: 元のグループでは隣のタブが選ばれた状態になり、移動先が表示される
    $vm.SelectedGroup = $g1
    $vm.OpenInNewTab("$d\mvg"); $mv2 = $vm.SelectedTab
    $vm.MoveTabToGroup($mv2, $g2)
    Check 'move to group: moving the active tab shows the destination with that tab active' (($vm.SelectedGroup -eq $g2) -and ($vm.SelectedTab -eq $mv2))
    $vm.SelectedGroup = $g1
    Check 'move to group: the source group falls back to a neighbor of the moved tab' (($null -ne $vm.SelectedTab) -and ($g1.Tabs -contains $vm.SelectedTab))
    # ペインタブも同じ
    $vm.SelectedGroup = $g1
    [void]$vm.CreatePanes([string[]]@("$d2", "$d\sub")); $pane = $vm.SelectedTab
    $vm.MoveTabToGroup($pane, $g2)
    Check 'move to group: a pane tab moves too, and the destination shows it as the active tab' (($vm.SelectedGroup -eq $g2) -and ($vm.SelectedTab -eq $pane))

    # ── タブの複数段表示(設定)の保存と復元
    Check 'multi-row tabs: off by default' ((-not $vm.MultiRowTabs) -and (-not ([Activator]::CreateInstance($AS)).MultiRowTabs))
    $vm.MultiRowTabs = $true
    $stM = $vm.ToState()
    Check 'multi-row tabs: saved in the state' ($stM.MultiRowTabs -eq $true)
    $vmM = [Activator]::CreateInstance($MVM, [object[]]@($stM))
    Check 'multi-row tabs: restored from the state' ($vmM.MultiRowTabs -eq $true)
    $vm.MultiRowTabs = $false
    Check 'multi-row tabs: turned off again' (-not $vm.ToState().MultiRowTabs)

    # ── グループの並べ替え・色・デフォルトグループ(並べ替えても同じグループのまま)
    $vmG = [Activator]::CreateInstance($MVM, [object[]]@([Activator]::CreateInstance($AS)))
    $def = $vmG.Groups[0]
    $vmG.AddGroup('G-A'); $ga = $vmG.SelectedGroup
    $vmG.AddGroup('G-B'); $gb = $vmG.SelectedGroup
    function Names { @($vmG.Groups | ForEach-Object { $_.Name }) -join ',' }

    # ── 同じ名前のタブに添える親フォルダ名
    $TT = $asm.GetType('KT_Explorer.Models.TabTitles')
    function Hints([string[]]$paths) {
        $list = [System.Collections.Generic.List[string]]::new(); foreach ($p in $paths) { $list.Add($p) }
        @($TT.GetMethod('ParentHints').Invoke($null, [object[]]@(, $list))) -join '|'
    }
    Check 'hint: different parents' ((Hints 'C:\w\ProjectA\images', 'C:\w\ProjectB\images') -eq 'ProjectA|ProjectB')
    Check 'hint: same parent name -> the level that differs' ((Hints 'C:\ProjectA\src\images', 'C:\ProjectB\src\images') -eq 'ProjectA|ProjectB')
    Check 'hint: only for duplicated names' ((Hints 'C:\a\images', 'C:\a\docs', 'C:\b\IMAGES') -eq 'a||b')
    Check 'hint: the same folder twice gets nothing' ((Hints 'C:\a\images', 'C:\a\images\') -eq '|')
    Check 'hint: the same folder twice plus another place' ((Hints 'C:\a\images', 'C:\a\images', 'C:\b\images') -eq 'a|a|b')
    Check 'hint: directly under a drive shows the drive' ((Hints 'C:\images', 'D:\images') -eq 'C:|D:')
    Check 'hint: three tabs, each from its first differing level' ((Hints 'C:\A\src\x', 'C:\B\src\x', 'C:\C\lib\x') -eq 'A|B|lib')

    $h = Join-Path $d 'hint'
    New-Item -ItemType Directory "$h\PA\images", "$h\PB\images", "$h\other" -Force | Out-Null
    $vmH = [Activator]::CreateInstance($MVM, [object[]]@([Activator]::CreateInstance($asm.GetType('KT_Explorer.Services.AppState'))))
    $vmH.OpenInNewTab("$h\PA\images"); $t1 = $vmH.SelectedTab
    Check 'hint: on by default, nothing while the name is unique' ($vmH.ShowParentNames -and $t1.TitleHint -eq '')
    $vmH.OpenInNewTab("$h\PB\images"); $t2 = $vmH.SelectedTab
    Check 'hint: a second tab with the same name gives both a hint' (($t1.TitleHint -eq 'PA') -and ($t2.TitleHint -eq 'PB'))
    $t2.NavigateTo("$h\other")
    Check 'hint: gone when the names differ again' (($t1.TitleHint -eq '') -and ($t2.TitleHint -eq ''))
    $t2.NavigateTo("$h\PB\images")
    Check 'hint: back after navigating to the same name' (($t1.TitleHint -eq 'PA') -and ($t2.TitleHint -eq 'PB'))
    $vmH.ShowParentNames = $false
    Check 'hint: off removes them, and is saved' (($t1.TitleHint -eq '') -and ($t2.TitleHint -eq '') -and (-not $vmH.ToState().ShowParentNames))
    $vmH.ShowParentNames = $true
    Check 'hint: on again brings them back' (($t1.TitleHint -eq 'PA') -and ($t2.TitleHint -eq 'PB'))
    $src = $vmH.SelectedGroup; $vmH.AddGroup('H2'); $other = $vmH.SelectedGroup
    $vmH.MoveTabToGroup($t2, $other)
    Check 'hint: only within one group (moving a tab away clears both)' (($t1.TitleHint -eq '') -and ($t2.TitleHint -eq ''))
    $st = $vmH.ToState(); $st.ShowParentNames = $false
    $vmH2 = [Activator]::CreateInstance($MVM, [object[]]@($st))
    Check 'hint: the off setting is restored' (-not $vmH2.ShowParentNames)
    Check 'group order: added in order' ((Names) -eq "$($def.Name),G-A,G-B")
    $vmG.ReorderGroup($gb, $def)
    Check 'group order: drag B onto the first -> B comes first' ((Names) -eq "G-B,$($def.Name),G-A")
    Check 'group order: the selected group does not change' ($vmG.SelectedGroup -eq $gb)
    Check 'group order: the default group is still the same group (not by position)' ($def.IsDefault -and -not $vmG.Groups[0].IsDefault)
    $vmG.ReorderGroup($ga, $ga)
    Check 'group order: dropping onto itself does nothing' ((Names) -eq "G-B,$($def.Name),G-A")
    $vmG.SetGroupColor($ga, '#30A46C')
    function Rgb($b) { '{0},{1},{2}' -f $b.Color.R, $b.Color.G, $b.Color.B }
    Check 'group color: set (outline = the color, selected fill = 24% of it)' (($ga.Color -eq '#30A46C') -and ((Rgb $ga.OutlineBrush) -eq '48,164,108') -and ((Rgb $ga.SelectedOutlineBrush) -eq '48,164,108') -and ((Rgb $ga.SelectedFillBrush) -eq '205,233,220'))
    Check 'group without a color: gray outline, blue outline and light blue fill when selected' (((Rgb $gb.OutlineBrush) -eq '195,201,209') -and ((Rgb $gb.SelectedOutlineBrush) -eq '59,130,214') -and ((Rgb $gb.SelectedFillBrush) -eq '220,235,250'))
    $vmG.SetGroupColor($gb, '#abc')
    Check 'group color: a value that is not #RRGGBB becomes no color' ($null -eq $gb.Color)
    # 一覧の背景: 表示中のグループの色を、ごく薄く(約 6%)。色が無ければ白。任意の色も使える
    function Bg { $c = $vmG.ViewBackground.Color; '{0},{1},{2}' -f $c.R, $c.G, $c.B }
    $vmG.SelectedGroup = $ga
    Check 'view background: the shown group color, very pale (#30A46C -> 243,250,246)' ((Bg) -eq '243,250,246')
    function Hd { $c = $vmG.ViewHeaderBackground.Color; '{0},{1},{2},{3}' -f $c.A, $c.R, $c.G, $c.B }
    Check 'column header: a little darker than the list (#30A46C 24% -> 205,233,220)' ((Hd) -eq '255,205,233,220')
    $vmG.SelectedGroup = $gb
    Check 'view background: a group without a color is white' ((Bg) -eq '255,255,255')
    Check 'column header: transparent for a group without a color' ($vmG.ViewHeaderBackground.Color.A -eq 0)
    $vmG.SetGroupColor($gb, '#000000')
    Check 'view background: even black stays readable (6% only)' (((Bg) -eq '240,240,240') -or ((Bg) -eq '239,239,239'))
    $vmG.SetGroupColor($gb, $null)
    Check 'view background: clearing the color of the shown group returns to white' ((Bg) -eq '255,255,255')
    $stG = $vmG.ToState()
    Check 'group state: order, color and the default mark are saved' (($stG.Groups[0].Name -eq 'G-B') -and ($stG.Groups[2].Color -eq '#30A46C') -and $stG.Groups[1].IsDefault -and -not $stG.Groups[0].IsDefault)
    $vmG2 = [Activator]::CreateInstance($MVM, [object[]]@($stG))
    $d2 = $vmG2.Groups | Where-Object { $_.IsDefault }
    Check 'group state: restored (order, color, default group)' (((@($vmG2.Groups | ForEach-Object { $_.Name }) -join ',') -eq "G-B,$($def.Name),G-A") -and ($vmG2.Groups[2].Color -eq '#30A46C') -and (@($d2).Count -eq 1) -and ($d2.Name -eq $def.Name))
    New-Item -ItemType Directory "$d\import" -Force | Out-Null
    $vmG2.ImportFolder("$d\import")
    Check 'default group: an imported Explorer folder goes into the default group even after reordering' (($vmG2.SelectedGroup.IsDefault) -and (@($vmG2.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\import" }).Count -eq 1))
    Check 'default group: cannot be deleted even when it is not first' ($vmG2.DeleteGroup($d2) -eq $false)
    # 古い状態(デフォルトの印が無い)は、先頭がデフォルト
    $old = [System.Text.Json.JsonSerializer]::Deserialize('{"Groups":[{"Name":"x","TabPaths":[]},{"Name":"y","TabPaths":[]}]}', $AS)
    $vmO = [Activator]::CreateInstance($MVM, [object[]]@($old))
    Check 'default group: without a saved mark, the first group is the default' ($vmO.Groups[0].IsDefault -and -not $vmO.Groups[1].IsDefault)
}
finally {
    Remove-TestDir $d
}
Show-Result
