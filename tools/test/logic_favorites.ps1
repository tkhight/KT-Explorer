# お気に入りの整理(並べ替え・グループへの移動・区切り線・複製・削除)と、状態への保存・復元のロジックの検証。
# 画面は出さず、ビルドした DLL を直接呼ぶ。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_favorites.ps1'
# 操作するフォルダは %TEMP%\kt_test_favorites の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$FavT = $asm.GetType('KT_Explorer.Models.Favorite')
$KindT = $asm.GetType('KT_Explorer.Models.FavoriteKind')
$FT = $asm.GetType('KT_Explorer.Services.FavoriteTree')
$FSt = $asm.GetType('KT_Explorer.Services.FavoriteState')
$AS = $asm.GetType('KT_Explorer.Services.AppState')
$MVM = $asm.GetType('KT_Explorer.ViewModels.MainViewModel')
$colT = [System.Collections.ObjectModel.ObservableCollection`1].MakeGenericType($FavT)

# 空のコレクションは、関数から返すと、PowerShell が展開して「何も返さない」ことになるため、, で包んで返す
function New-Root { , [Activator]::CreateInstance($colT) }
function Fav([string]$name, [string]$path) { [Activator]::CreateInstance($FavT, [object[]]@($name, $path)) }
function Op([string]$method, [object[]]$args_) { $FT.GetMethod($method).Invoke($null, $args_) }
function Order($list) { (@($list | ForEach-Object { if ($_.IsSeparator) { '|' } elseif ($_.IsGroup) { "[$($_.Name)]" } else { $_.Name } }) -join ',') }

$d = New-TestDir 'kt_test_favorites'
foreach ($n in 'dA', 'dB', 'dC') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
try {
    # ── 並べ替え(同じ並びの中)
    $root = New-Root
    $a = Fav 'A' "$d\dA"; $b = Fav 'B' "$d\dB"; $c = Fav 'C' "$d\dC"
    foreach ($f in $a, $b, $c) { $root.Add($f) }
    Check 'move up: B goes above A' ((Op 'MoveUp' @($root, $b)) -and (Order $root) -eq 'B,A,C')
    Check 'move up at the top: nothing to do' (-not (Op 'MoveUp' @($root, $b)))
    Check 'move down: A goes below C' ((Op 'MoveDown' @($root, $a)) -and (Order $root) -eq 'B,C,A')
    Check 'move down at the bottom: nothing to do' (-not (Op 'MoveDown' @($root, $a)))

    # ── グループと区切り線
    $g = $FavT::NewGroup('G')
    $root.Add($g)
    Check 'a group is added (at the end)' ((Order $root) -eq 'B,C,A,[G]')
    Check 'move C into the group' ((Op 'MoveTo' @($root, $c, $g)) -and (Order $root) -eq 'B,A,[G]' -and (Order $g.Children) -eq 'C')
    Check 'move up/down works inside a group, too' ((Op 'MoveTo' @($root, $a, $g)) -and (Order $g.Children) -eq 'C,A' -and (Op 'MoveUp' @($root, $a)) -and (Order $g.Children) -eq 'A,C')
    Check 'a group cannot be put into a group (no nesting)' (-not (Op 'MoveTo' @($root, $g, $g)))
    $g2 = $FavT::NewGroup('G2'); $root.Add($g2)
    Check 'a group cannot be put into another group either' (-not (Op 'MoveTo' @($root, $g2, $g)))
    Check 'move out of the group: back to the top level (at the end)' ((Op 'MoveTo' @($root, $c, $null)) -and (Order $g.Children) -eq 'A' -and (Order $root) -eq 'B,[G],[G2],C')
    Check 'moving to where it already is: nothing to do' (-not (Op 'MoveTo' @($root, $b, $null)))
    $sep = Op 'InsertSeparatorAfter' @($root, $b)
    Check 'a separator is inserted right after the selected item' ((Order $root) -eq 'B,|,[G],[G2],C' -and $sep.IsSeparator)
    $sep2 = Op 'InsertSeparatorAfter' @($root, $a)
    Check 'a separator after an item inside a group goes inside that group' ((Order $g.Children) -eq 'A,|')
    $sep3 = Op 'InsertSeparatorAfter' @($root, $null)
    Check 'a separator with nothing selected goes to the end' ((Order $root) -eq 'B,|,[G],[G2],C,|')

    # ── すべてのフォルダ(グループの中も含む)
    Check 'folders: all folder favorites including those in groups' ((@(Op 'Folders' @(,$root) | ForEach-Object { $_.Name }) -join ',') -eq 'B,A,C')

    # ── 複製は、元と独立している(整理ウィンドウは、複製を編集して、OK で反映する)
    $copy = Op 'Clone' @(,$root)
    $copy[0].Name = 'CHANGED'; [void]$copy[2].Children.Clear()
    Check 'a clone is independent of the original' (((Order $root) -eq 'B,|,[G],[G2],C,|') -and ((Order $g.Children) -eq 'A,|'))

    # ── 削除(グループなら、中身ごと)
    Check 'remove a group together with its contents' ((Op 'Remove' @($root, $g)) -and (Order $root) -eq 'B,|,[G2],C,|' -and @(Op 'Folders' @(,$root)).Count -eq 2)

    # ── 状態への保存と、復元(グループ・区切り線を含む)
    $state = [Activator]::CreateInstance($AS)
    function St([string]$name, [string]$path, [string]$kind = 'Folder') {
        $s = [Activator]::CreateInstance($FSt); $s.Name = $name; $s.Path = $path; $s.Kind = [Enum]::Parse($KindT, $kind); $s }
    $grp = St 'Grp' '' 'Group'
    $grp.Children.Add((St 'in1' "$d\dB")); $grp.Children.Add((St '' '' 'Separator')); $grp.Children.Add((St 'in2' "$d\dC"))
    $state.Favorites.Add((St 'top' "$d\dA")); $state.Favorites.Add((St '' '' 'Separator')); $state.Favorites.Add($grp)
    $vm = [Activator]::CreateInstance($MVM, [object[]]@($state))
    Check 'state: loaded with groups and separators' ((Order $vm.Favorites) -eq 'top,|,[Grp]' -and (Order $vm.Favorites[2].Children) -eq 'in1,|,in2')
    $out = $vm.ToState()
    $fs = $out.Favorites
    Check 'state: saved back with the same structure' ($fs.Count -eq 3 -and "$($fs[1].Kind)" -eq 'Separator' -and "$($fs[2].Kind)" -eq 'Group' -and $fs[2].Children.Count -eq 3 -and $fs[2].Children[2].Path -eq "$d\dC")

    # 古い状態ファイル(Kind が無い、フォルダだけ)は、フォルダとして読める
    $old = [Activator]::CreateInstance($AS)
    $o = [Activator]::CreateInstance($FSt); $o.Name = 'old'; $o.Path = "$d\dA"; $old.Favorites.Add($o)
    $vmOld = [Activator]::CreateInstance($MVM, [object[]]@($old))
    Check 'state: an old favorite without a kind is a folder' ($vmOld.Favorites.Count -eq 1 -and $vmOld.Favorites[0].IsFolder -and $vmOld.Favorites[0].Name -eq 'old')

    # ── 追加・削除・名前変更への追従は、グループの中にも及ぶ
    $vm.AddFavorite("$d\dC")
    Check 'add: a folder that is already in a group is not added again' ($vm.Favorites.Count -eq 3)
    New-Item -ItemType Directory (Join-Path $d 'dNew') | Out-Null
    $vm.AddFavorite("$d\dNew")
    Check 'add: a new folder goes to the end of the top level' ($vm.Favorites.Count -eq 4 -and $vm.Favorites[3].Name -eq 'dNew')
    $vm.OnFolderRenamed("$d\dC", "$d\dC2")
    Check 'rename follow: a favorite inside a group follows the renamed folder' ($vm.Favorites[2].Children[2].Path -eq "$d\dC2")
    $vm.RemoveFavorite($vm.Favorites[2].Children[0])
    Check 'remove: a favorite inside a group can be removed' ((Order $vm.Favorites[2].Children) -eq '|,in2')

    # ── 整理後のものに、まるごと置き換える
    $new = New-Root; $x = Fav 'X' "$d\dA"; $new.Add($x)
    $vm.ReplaceFavorites($new)
    Check 'replace: the favorites are replaced as a whole' ($vm.Favorites.Count -eq 1 -and $vm.Favorites[0].Name -eq 'X')
}
finally {
    Remove-TestDir $d
}
Show-Result
