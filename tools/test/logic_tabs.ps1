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
    Check 'after undo the tab moves to the nearest existing parent' (-not ((Tabs) -contains "$d\x2\y"))

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
}
finally {
    Remove-TestDir $d
}
Show-Result
