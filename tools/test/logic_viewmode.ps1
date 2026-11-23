# 表示の切り替え(詳細・アイコン・一覧)のロジックの検証: サムネイルの取得(ThumbnailService / FileEntry.Thumbnail)と、
# タブごとの表示・新しいタブの初期の表示・状態への保存と復元(古い状態ファイルとの互換を含む)。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_viewmode.ps1'
# 操作するフォルダは %TEMP%\kt_test_viewmode の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$TS = $asm.GetType('KT_Explorer.Services.ThumbnailService')
$FE = $asm.GetType('KT_Explorer.Models.FileEntry')
$ViewModeT = $asm.GetType('KT_Explorer.Models.ViewMode')   # 注意: PowerShell の変数名は大文字小文字を区別しない。$vm(アプリの本体)と同じ名前にしない
$MVM = $asm.GetType('KT_Explorer.ViewModels.MainViewModel')
$AS = $asm.GetType('KT_Explorer.Services.AppState')
$SS = $asm.GetType('KT_Explorer.Services.SortSettings')

function Get-Thumb([string]$path, [int]$size = 96) {
    $task = $TS.GetMethod('GetAsync').Invoke($null, [object[]]@($path, $size))
    if (-not $task.Wait(10000)) { return 'timeout' }
    $task.Result
}

$d = New-TestDir 'kt_test_viewmode'
try {
    # ── サムネイル(画像は、中身のサムネイル。それ以外は、大きなアイコン。壊れたものは、null)
    $pixels = New-Object byte[] (64 * 64 * 4); for ($i = 0; $i -lt $pixels.Length; $i += 4) { $pixels[$i] = 200; $pixels[$i + 1] = 80; $pixels[$i + 2] = 30; $pixels[$i + 3] = 255 }
    $bmp = [System.Windows.Media.Imaging.BitmapSource]::Create(64, 64, 96, 96, [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, 256)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [System.IO.File]::Create("$d\picture.png"); $enc.Save($fs); $fs.Dispose()
    Set-Content -LiteralPath "$d\note.txt" -Value 'x'
    New-Item -ItemType Directory "$d\folder" | Out-Null

    $t = Get-Thumb "$d\picture.png"
    Check 'thumbnail: an image gives a bitmap of a usable size (not a 16px icon)' (($t -isnot [string]) -and $null -ne $t -and $t.PixelWidth -ge 32 -and $t.IsFrozen)
    $t = Get-Thumb "$d\note.txt"
    Check 'thumbnail: a text file gives a large icon' (($t -isnot [string]) -and $null -ne $t -and $t.PixelWidth -ge 32)
    $t = Get-Thumb "$d\folder"
    Check 'thumbnail: a folder gives a large icon' (($t -isnot [string]) -and $null -ne $t -and $t.PixelWidth -ge 32)
    $t = Get-Thumb "$d\no_such_file.png"
    Check 'thumbnail: a path that does not exist gives null (no exception)' ($null -eq $t)

    # 同時にたくさん頼んでも、全部返る(専用の STA スレッドで順に処理する)
    $tasks = 1..12 | ForEach-Object { $TS.GetMethod('GetAsync').Invoke($null, [object[]]@("$d\picture.png", 96)) }
    $all = [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($tasks), 15000)
    Check 'thumbnail: 12 requests at once all complete' ($all -and @($tasks | Where-Object { $_.Result }).Count -eq 12)

    # FileEntry.Thumbnail: 最初は小さなアイコン。読み込めたら、大きなものに替わる
    $entry = [System.Activator]::CreateInstance($FE, [object[]]@([System.IO.FileInfo]::new("$d\picture.png"), $null))
    $first = $entry.Thumbnail
    Check 'FileEntry.Thumbnail: returns something at once (the small icon)' ($null -ne $first)
    $large = $false
    for ($i = 0; $i -lt 100 -and -not $large; $i++) { Start-Sleep -Milliseconds 100; $large = $entry.Thumbnail.PixelWidth -ge 32 }
    Check 'FileEntry.Thumbnail: replaced by the large image once it is loaded' $large

    # ── 表示(詳細・アイコン・一覧)はタブごと。新しいタブは、最後に切り替えた表示で始まる
    function Mode($tab) { "$($tab.ViewMode)" }
    $state = [Activator]::CreateInstance($AS)
    $vm = [Activator]::CreateInstance($MVM, [object[]]@($state))
    New-Item -ItemType Directory "$d\v1", "$d\v2" | Out-Null
    $vm.OpenInNewTab("$d\v1"); $vm.OpenInNewTab("$d\v2")
    $t1 = $vm.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\v1" } | Select-Object -First 1
    $t2 = $vm.SelectedGroup.Tabs | Where-Object { $_.Path -eq "$d\v2" } | Select-Object -First 1
    Check 'view mode: tabs start with the details view' ((Mode $t1) -eq 'Details' -and (Mode $t2) -eq 'Details')
    $tracker = [pscustomobject]@{ Count = 0 }
    $t1.add_ViewModeChanged([Action]{ $tracker.Count++ }.GetNewClosure())
    $t1.SetViewMode([Enum]::Parse($ViewModeT, 'Icons'))
    Check 'view mode: switching one tab does not change the other' ((Mode $t1) -eq 'Icons' -and (Mode $t2) -eq 'Details' -and $tracker.Count -eq 1)
    $t1.SetViewMode([Enum]::Parse($ViewModeT, 'Icons'))
    Check 'view mode: setting the same mode does not notify again' ($tracker.Count -eq 1)
    $vm.OpenInNewTab("$d\folder"); $t3 = $vm.SelectedTab
    Check 'view mode: a new tab starts with the last used view (Icons)' ((Mode $t3) -eq 'Icons')
    $vm.SetViewModeCommand.Execute('List')
    Check 'view mode: the command (Ctrl+Shift+3) switches the current tab' ((Mode $vm.CurrentFolder) -eq 'List')

    # ── 状態への保存と復元
    $out = $vm.ToState()
    $g0 = $out.Groups[0]
    $i1 = [array]::IndexOf(@($g0.TabPaths), "$d\v1"); $i2 = [array]::IndexOf(@($g0.TabPaths), "$d\v2"); $i3 = [array]::IndexOf(@($g0.TabPaths), "$d\folder")
    Check 'state: each tab saves its own view mode' (("$($g0.TabSorts[$i1].ViewMode)" -eq 'Icons') -and ("$($g0.TabSorts[$i2].ViewMode)" -eq 'Details') -and ("$($g0.TabSorts[$i3].ViewMode)" -eq 'List'))
    Check 'state: the default view for new tabs is saved' ("$($out.ViewMode)" -eq 'List')
    $vm2 = [Activator]::CreateInstance($MVM, [object[]]@($out))
    $r1 = $vm2.Groups[0].Tabs | Where-Object { $_.Path -eq "$d\v1" } | Select-Object -First 1
    $r2 = $vm2.Groups[0].Tabs | Where-Object { $_.Path -eq "$d\v2" } | Select-Object -First 1
    Check 'state: view modes are restored per tab' ((Mode $r1) -eq 'Icons' -and (Mode $r2) -eq 'Details')
    $vm2.OpenInNewTab("$d"); $r4 = $vm2.SelectedTab
    Check 'state: restoring the tabs does not overwrite the default (a new tab uses the saved default, List)' ((Mode $r4) -eq 'List')

    # 古い状態ファイル(表示の項目が無い)は、詳細として読める
    $SS.GetProperty('Instance').GetValue($null).DefaultViewMode = [Enum]::Parse($ViewModeT, 'Details')
    $old = [Activator]::CreateInstance($AS)
    $vmOld = [Activator]::CreateInstance($MVM, [object[]]@($old))
    Check 'state: an old state without view modes opens with the details view' ((Mode $vmOld.CurrentFolder) -eq 'Details')
}
finally {
    Remove-TestDir $d
}
Show-Result
