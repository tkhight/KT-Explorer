# ピン留めとタブの色のロジックの検証(画面は出さず、ビルドした DLL を直接呼ぶ)。
# ピン留めしたタブから別のフォルダへ移ると新しいタブで開く / 内部の移動(名前の変更への追従)は移動する / 状態への保存と復元 / 色。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_pin.ps1'
# 操作するフォルダは %TEMP%\kt_test_logic_pin の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) {
    Write-Output "ABORT: build not found: $($global:Kt.Dll)"
    exit 2
}
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$MVM = $asm.GetType('KT_Explorer.ViewModels.MainViewModel')
$AS  = $asm.GetType('KT_Explorer.Services.AppState')
$TC  = $asm.GetType('KT_Explorer.Models.TabColors')

$d = New-TestDir 'kt_test_logic_pin'
try {
    foreach ($n in 'home\sub', 'other', 'third') { New-Item -ItemType Directory (Join-Path $d $n) | Out-Null }
    $vm = [Activator]::CreateInstance($MVM, [object[]]@([Activator]::CreateInstance($AS)))
    function Tabs { @($vm.SelectedGroup.Tabs) }
    function Paths { @($vm.SelectedGroup.Tabs | ForEach-Object { $_.Path }) }

    $vm.OpenInNewTab("$d\home")
    $pinTab = $vm.SelectedTab
    $n0 = (Tabs).Count

    # ── 1) ピン留めしていないタブは、いままでどおり、そのタブの中で移動する
    $pinTab.NavigateTo("$d\other")
    Check '1) not pinned: navigating moves the same tab' (($pinTab.Path -eq "$d\other") -and ((Tabs).Count -eq $n0))
    $pinTab.NavigateTo("$d\home")

    # ── 2) ピン留め: 別のフォルダへ移ると、タブは動かず、新しいタブが右隣にできて選択される
    $vm.TogglePin($pinTab)
    Check '2) pinned flag is on' ($pinTab.IsPinned -eq $true)
    $idx = $vm.SelectedGroup.Tabs.IndexOf($pinTab)
    $pinTab.NavigateTo("$d\home\sub")
    Check '2) the pinned tab stays at its folder' ($pinTab.Path -eq "$d\home")
    Check '2) a new tab was opened' ((Tabs).Count -eq $n0 + 1)
    $new = $vm.SelectedGroup.Tabs[$idx + 1]
    Check '2) the new tab is right next to the pinned one, at the destination' ($new.Path -eq "$d\home\sub")
    Check '2) the new tab is selected' ($vm.SelectedTab -eq $new)
    Check '2) the new tab is not pinned' ($new.IsPinned -eq $false)

    # ── 3) ピン留めしたタブでも、同じフォルダの再読み込みは新しいタブにしない
    $c = (Tabs).Count
    $pinTab.NavigateTo("$d\home")
    Check '3) same folder: no new tab' ((Tabs).Count -eq $c)

    # ── 4) 上へ・パス欄・ツリーなど、通常の移動の入り口(NavigateTo)はすべて新しいタブになる。上へ:
    $vm.SelectedTab = $pinTab
    $pinTab.GoUp()
    Check '4) up from the pinned folder opens the parent in a new tab' (((Tabs).Count -eq $c + 1) -and ($vm.SelectedTab.Path -eq $d) -and ($pinTab.Path -eq "$d\home"))
    $c = (Tabs).Count
    $vm.SelectedTab = $pinTab
    $ok = $vm.NavigateToText("$d\third")
    Check '4) typing a path opens it in a new tab' (($ok -eq $true) -and ((Tabs).Count -eq $c + 1) -and ($vm.SelectedTab.Path -eq "$d\third"))

    # ── 5) 内部の移動(recordHistory = false: 名前の変更への追従など)は、ピン留めしたタブも移動する
    $pinTab.NavigateTo("$d\other", $false)
    Check '5) internal move (rename follow etc.) moves the pinned tab' ($pinTab.Path -eq "$d\other")
    $pinTab.NavigateTo("$d\home", $false)

    # ── 6) ピン留めを外すと、また、そのタブの中で移動する
    $vm.TogglePin($pinTab)
    Check '6) unpinned' ($pinTab.IsPinned -eq $false)
    $c = (Tabs).Count
    $pinTab.NavigateTo("$d\other")
    Check '6) after unpinning, navigating moves the same tab' (($pinTab.Path -eq "$d\other") -and ((Tabs).Count -eq $c))
    $pinTab.NavigateTo("$d\home")

    # ── 6b) ピン留めしたタブは閉じられない(右クリック・中クリック・Ctrl+W・ロッカーは、すべて CloseTab を通る)
    $vm.TogglePin($pinTab)
    $c = (Tabs).Count
    $vm.CloseTabCommand.Execute($pinTab)
    Check '6b) a pinned tab is not closed (explicit target: right-click menu / middle click)' (((Tabs).Count -eq $c) -and ((Paths) -contains "$d\home"))
    $vm.SelectedTab = $pinTab
    $vm.CloseTabCommand.Execute($null)
    Check '6b) a pinned selected tab is not closed (Ctrl+W / rocker gesture)' (((Tabs).Count -eq $c) -and ($vm.SelectedTab -eq $pinTab))
    Check '6b) nothing was added to the reopen history' (-not $vm.CanReopenClosedTab)
    $vm.AddGroup('GP'); $gp = $vm.SelectedGroup
    $vm.SelectedGroup = $vm.Groups[0]
    $vm.MoveTabToGroup($pinTab, $gp)
    Check '6b) a group that contains a pinned tab cannot be deleted' (($vm.DeleteGroup($gp) -eq $false) -and ($vm.Groups -contains $gp))
    $vm.TogglePin($pinTab)
    Check '6b) after unpinning, the tab can be closed' (($vm.CloseTabCommand.Execute($pinTab) -eq $null) -and (-not ($gp.Tabs -contains $pinTab)))
    Check '6b) and then the group (now with no pinned tab) can be deleted' (($vm.DeleteGroup($gp) -eq $true) -and (-not ($vm.Groups -contains $gp)))
    $vm.SelectedGroup = $vm.Groups[0]
    $vm.OpenInNewTab("$d\home"); $pinTab = $vm.SelectedTab

    # ── 7) 色: 設定・取り消し・パレット以外は色なし
    $vm.SetTabColor($pinTab, '#E5484D')
    Check '7) color is set' ($pinTab.TabColor -eq '#E5484D')
    Check '7) the color brush is not transparent' ($pinTab.TabColorBrush.Color.A -eq 255 -and $pinTab.TabColorBrush.Color.R -eq 0xE5)
    Check '7) the tint brush is translucent' ($pinTab.TabTintBrush.Color.A -lt 255 -and $pinTab.TabTintBrush.Color.A -gt 0)
    $vm.SetTabColor($pinTab, '#123456')
    Check '7) a color outside the palette becomes no color' ($null -eq $pinTab.TabColor)
    Check '7) no color: the brush is transparent' ($pinTab.TabColorBrush -eq [System.Windows.Media.Brushes]::Transparent)
    $vm.SetTabColor($pinTab, '#30A46C'); $vm.SetTabColor($pinTab, $null)
    Check '7) color can be cleared' ($null -eq $pinTab.TabColor)
    Check '7) the palette has several colors' (@($TC.GetField('Palette').GetValue($null)).Count -ge 6)

    # ── 8) 複製: 色は引き継ぎ、ピン留めは引き継がない
    $vm.TogglePin($pinTab); $vm.SetTabColor($pinTab, '#3E63DD')
    $vm.DuplicateTab($pinTab)
    $dup = $vm.SelectedTab
    Check '8) duplicate keeps the color' ($dup.TabColor -eq '#3E63DD')
    Check '8) duplicate is not pinned' ($dup.IsPinned -eq $false)

    # ── 9) 閉じて開き直すと、色も戻る(ピン留めしたタブは閉じられないので、ピン留めを外して閉じる。開き直したら、また留める)
    $vm.TogglePin($pinTab)
    $vm.CloseTabCommand.Execute($pinTab)
    $vm.ReopenClosedTabCommand.Execute($null)
    $re = $vm.SelectedTab
    Check '9) reopened tab: path and color are restored (and it is not pinned)' (($re.Path -eq "$d\home") -and (-not $re.IsPinned) -and ($re.TabColor -eq '#3E63DD'))
    $vm.TogglePin($re)

    # ── 10) 状態への保存と復元
    $state = $vm.ToState()
    $vm2 = [Activator]::CreateInstance($MVM, [object[]]@($state))
    $match = @($vm2.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\home" -and $_.IsPinned -and $_.TabColor -eq '#3E63DD' })
    Check '10) saved state restores the pinned and colored tab' ($match.Count -eq 1)
    $plain = @($vm2.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\third" })
    Check '10) a plain tab stays plain' (($plain.Count -ge 1) -and (-not $plain[0].IsPinned) -and ($null -eq $plain[0].TabColor))
    # 復元したピン留めタブも、移動すると新しいタブになる
    $c = @($vm2.SelectedGroup.Tabs).Count
    $match[0].NavigateTo("$d\other")
    Check '10) a restored pinned tab opens a new tab too' ((@($vm2.SelectedGroup.Tabs).Count -eq $c + 1) -and ($match[0].Path -eq "$d\home"))

    # ── 11) 古い状態ファイル(ピン留め・色の項目が無い)でも読み込める
    $json = '{"Groups":[{"Name":"g","IsDefault":true,"TabPaths":["' + ($d -replace '\\', '\\') + '"],"TabSorts":[{"Column":0,"Descending":false,"ViewMode":0}],"SelectedIndex":0}]}'
    $old = [System.Text.Json.JsonSerializer]::Deserialize($json, $AS)
    $vm3 = [Activator]::CreateInstance($MVM, [object[]]@($old))
    $t3 = $vm3.SelectedGroup.Tabs[0]
    Check '11) an old state without pin/color loads as unpinned, no color' (($t3.Path -eq $d) -and (-not $t3.IsPinned) -and ($null -eq $t3.TabColor))
}
finally {
    Remove-TestDir $d
}
Show-Result
