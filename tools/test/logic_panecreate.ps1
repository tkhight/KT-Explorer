# 指定ペインの作成(PaneRequest の条件・フォルダ入力の整理、MainViewModel.CreatePanes)のロジックの検証。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_panecreate.ps1'
# 操作するフォルダは %TEMP%\kt_test_panecreate の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$PR = $asm.GetType('KT_Explorer.Services.PaneRequest')

function Can([string[]]$texts) { [bool]$PR.GetMethod('CanCreateFromFolders').Invoke($null, [object[]]@(, [string[]]$texts)) }
function Clean([string[]]$texts) { $PR.GetMethod('CleanFolders').Invoke($null, [object[]]@(, [string[]]$texts)) }

$d = New-TestDir 'kt_test_panecreate'
try {
    New-Item -ItemType Directory "$d\a", "$d\b", "$d\c" | Out-Null

    Check 'count: 2 and 3 are valid, 1 and 4 are not' (($PR.GetMethod('IsValidCount').Invoke($null, @(2))) -and ($PR.GetMethod('IsValidCount').Invoke($null, @(3))) -and (-not $PR.GetMethod('IsValidCount').Invoke($null, @(1))) -and (-not $PR.GetMethod('IsValidCount').Invoke($null, @(4))))

    Check 'folders: two existing folders -> can create' (Can @("$d\a", "$d\b", ''))
    Check 'folders: three existing folders -> can create' (Can @("$d\a", "$d\b", "$d\c"))
    Check 'folders: only one folder -> cannot create' (-not (Can @("$d\a", '', '')))
    Check 'folders: nothing -> cannot create' (-not (Can @('', '', '')))
    Check 'folders: a missing folder blocks creating, even with two others' (-not (Can @("$d\a", "$d\b", "$d\nope")))
    Check 'folders: blank boxes in the middle are skipped (1st and 3rd)' (Can @("$d\a", '', "$d\c"))
    Check 'folders: quotes and spaces around a path are removed' (Can @("  `"$d\a`"  ", "$d\b", ''))

    $r = Clean @("$d\a", '', "$d\nope")
    Check 'clean: valid paths and the invalid slot numbers (slot 2, counted from 0)' ((@($r.Item1).Count -eq 1) -and (@($r.Item2).Count -eq 1) -and ($r.Item2[0] -eq 2))
    $r = Clean @("$d\a\", "$d\b", '')
    Check 'clean: a trailing backslash is normalised away' ($r.Item1[0] -eq "$d\a")

    # ── MainViewModel.CreatePanes(ペインタブが、カレントタブの右隣にできる)
    $MVM = $asm.GetType('KT_Explorer.ViewModels.MainViewModel')
    $AS = $asm.GetType('KT_Explorer.Services.AppState')
    $vm = [Activator]::CreateInstance($MVM, [object[]]@([Activator]::CreateInstance($AS)))
    $before = @($vm.SelectedGroup.Tabs).Count
    $list = [System.Collections.Generic.List[string]]::new(); $list.Add("$d\a"); $list.Add("$d\b")
    Check 'CreatePanes: two folders -> true' ($vm.CreatePanes($list) -eq $true)
    Check 'CreatePanes: a pane tab was added and selected, with the two folders' ((@($vm.SelectedGroup.Tabs).Count -eq $before + 1) -and ($vm.SelectedTab.GetType().Name -eq 'PaneTabViewModel') -and (@($vm.SelectedTab.Panes).Count -eq 2) -and ($vm.SelectedTab.Panes[0].Path -eq "$d\a") -and ($vm.SelectedTab.Panes[1].Path -eq "$d\b"))
    $one = [System.Collections.Generic.List[string]]::new(); $one.Add("$d\a")
    Check 'CreatePanes: one folder -> false, nothing added' (($vm.CreatePanes($one) -eq $false) -and (@($vm.SelectedGroup.Tabs).Count -eq $before + 1))
    $bad = [System.Collections.Generic.List[string]]::new(); $bad.Add("$d\a"); $bad.Add("$d\nope")
    Check 'CreatePanes: a missing folder -> false, nothing added' (($vm.CreatePanes($bad) -eq $false) -and (@($vm.SelectedGroup.Tabs).Count -eq $before + 1))
    $three = [System.Collections.Generic.List[string]]::new(); $three.Add("$d\a"); $three.Add("$d\b"); $three.Add("$d\c")
    Check 'CreatePanes: three folders -> true, three panes' (($vm.CreatePanes($three) -eq $true) -and (@($vm.SelectedTab.Panes).Count -eq 3))
    $four = [System.Collections.Generic.List[string]]::new(); $four.Add("$d\a"); $four.Add("$d\b"); $four.Add("$d\c"); $four.Add("$d\a")
    Check 'CreatePanes: four folders -> false' ($vm.CreatePanes($four) -eq $false)
}
finally {
    Remove-TestDir $d
}
Show-Result
