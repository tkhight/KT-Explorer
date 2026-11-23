# フォルダのサイズの記憶(FolderSizeCache)と、サムネイルの記憶・アイコンの大きさ(ThumbnailService / ViewSettings)のロジックの検証。
# 画面は出さず、ビルドした DLL を直接呼ぶ。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_cache.ps1'
# 操作するフォルダは %TEMP%\kt_test_cache の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$FC = $asm.GetType('KT_Explorer.Services.FolderSizeCache')
$FOS = $asm.GetType('KT_Explorer.Services.FileOperationService')
$TS = $asm.GetType('KT_Explorer.Services.ThumbnailService')
$VS = $asm.GetType('KT_Explorer.Services.ViewSettings')
$IS = $asm.GetType('KT_Explorer.Services.IconSize')
$FE = $asm.GetType('KT_Explorer.Models.FileEntry')

function Cache-Has([string]$path) { $a = [object[]]@($path, 0L); [bool]$FC.GetMethod('TryGet').Invoke($null, $a) }
function Cache-Set([string]$path, [long]$size) { [void]$FC.GetMethod('Set').Invoke($null, [object[]]@($path, $size)) }
function Cache-Size([string]$path) { $a = [object[]]@($path, 0L); [void]$FC.GetMethod('TryGet').Invoke($null, $a); $a[1] }

$d = New-TestDir 'kt_test_cache'
try {
    # ── フォルダのサイズの記憶: 記憶・取り出し・期限
    $FC.GetMethod('Clear').Invoke($null, @())
    Cache-Set "$d\a" 1234
    Check 'size cache: a stored size comes back' ((Cache-Has "$d\a") -and (Cache-Size "$d\a") -eq 1234)
    Check 'size cache: the path is not case-sensitive and ignores a trailing backslash' (Cache-Has "$($d.ToUpper())\A\")

    $clock = [pscustomobject]@{ Now = [datetime]::UtcNow }
    $FC.GetProperty('Now').SetValue($null, [Func[datetime]]{ $clock.Now }.GetNewClosure())
    Cache-Set "$d\old" 10
    $clock.Now = $clock.Now.AddMinutes(4)
    Check 'size cache: still valid after 4 minutes' (Cache-Has "$d\old")
    $clock.Now = $clock.Now.AddMinutes(2)
    Check 'size cache: dropped after more than 5 minutes' (-not (Cache-Has "$d\old"))
    $FC.GetProperty('Now').SetValue($null, [Func[datetime]]{ [datetime]::UtcNow })

    # ── 無効化: 上位のフォルダ、中のフォルダ
    $FC.GetMethod('Clear').Invoke($null, @())
    foreach ($p in $d, "$d\p", "$d\p\child", "$d\p\child\deep", "$d\other") { Cache-Set $p 1 }
    $FC.GetMethod('InvalidateUp').Invoke($null, @("$d\p\child"))
    Check 'invalidate up: the folder and its ancestors are dropped' ((-not (Cache-Has "$d\p\child")) -and (-not (Cache-Has "$d\p")) -and (-not (Cache-Has $d)))
    Check 'invalidate up: folders inside, and siblings, are kept' ((Cache-Has "$d\p\child\deep") -and (Cache-Has "$d\other"))
    foreach ($p in $d, "$d\p", "$d\p\child", "$d\p\child\deep", "$d\other") { Cache-Set $p 1 }
    $FC.GetMethod('InvalidateTree').Invoke($null, @("$d\p"))
    Check 'invalidate tree: the folder, its ancestors and the folders inside are dropped' ((-not (Cache-Has "$d\p")) -and (-not (Cache-Has $d)) -and (-not (Cache-Has "$d\p\child")) -and (-not (Cache-Has "$d\p\child\deep")))
    Check 'invalidate tree: an unrelated sibling is kept' (Cache-Has "$d\other")
    Check 'invalidate: a folder whose name only starts the same is not touched ("pp" vs "p")' ((Cache-Set "$d\pp" 1) -eq $null -and ($FC.GetMethod('InvalidateTree').Invoke($null, @("$d\p")) -eq $null) -and (Cache-Has "$d\pp"))

    # ── ファイル操作で、記憶が無効になる(新規作成・名前の変更・移動)
    New-Item -ItemType Directory "$d\work\sub" | Out-Null
    $FC.GetMethod('Clear').Invoke($null, @())
    foreach ($p in $d, "$d\work", "$d\work\sub") { Cache-Set $p 1 }
    [void]$FOS.GetMethod('CreateFolder').Invoke($null, [object[]]@("$d\work", 'created', $false))
    Check 'file operation: creating a folder drops that folder and its ancestors' ((-not (Cache-Has "$d\work")) -and (-not (Cache-Has $d)) -and (Cache-Has "$d\work\sub"))
    foreach ($p in $d, "$d\work", "$d\work\sub") { Cache-Set $p 1 }
    [void]$FOS.GetMethod('Rename').Invoke($null, [object[]]@("$d\work\created", 'created2', $false))
    Check 'file operation: renaming drops the ancestors' ((-not (Cache-Has "$d\work")) -and (-not (Cache-Has $d)))
    # 移動(削除と同じく、元の項目が無くなり、移動先の中身が増える。永久削除は、確認ダイアログが出てテストが止まるため使わない)
    New-Item -ItemType Directory "$d\dest" | Out-Null
    foreach ($p in $d, "$d\work", "$d\work\sub", "$d\dest") { Cache-Set $p 1 }
    $moved = [System.Collections.Generic.List[string]]@("$d\work\sub")
    [void]$FOS.GetMethod('Transfer').Invoke($null, [object[]]@($moved, "$d\dest", $true, [IntPtr]::Zero, $false, $null))
    Check 'file operation: moving a folder drops its old place, the ancestors, and the destination' ((-not (Cache-Has "$d\work\sub")) -and (-not (Cache-Has "$d\work")) -and (-not (Cache-Has "$d\dest")) -and (-not (Cache-Has $d)))

    # ── サムネイルの記憶(更新日時の目印が違えば、別のものとして読み直す)
    $pixels = New-Object byte[] (64 * 64 * 4); for ($i = 0; $i -lt $pixels.Length; $i += 4) { $pixels[$i] = 200; $pixels[$i + 1] = 80; $pixels[$i + 2] = 30; $pixels[$i + 3] = 255 }
    $bmp = [System.Windows.Media.Imaging.BitmapSource]::Create(64, 64, 96, 96, [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, 256)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [System.IO.File]::Create("$d\picture.png"); $enc.Save($fs); $fs.Dispose()
    $TS.GetMethod('ClearCache').Invoke($null, @())
    function Cached([string]$path, [int]$size, [long]$stamp) { $a = [object[]]@($path, $size, $stamp, $null); [bool]$TS.GetMethod('TryGetCached').Invoke($null, $a) }
    Check 'thumbnail cache: empty at first' (-not (Cached "$d\picture.png" 96 1))
    $task = $TS.GetMethod('GetAsync', [Type[]]@([string], [int], [long])) ;
    $task = $TS.GetMethods() | Where-Object { $_.Name -eq 'GetAsync' -and $_.GetParameters().Count -eq 3 } | Select-Object -First 1
    $t1 = $task.Invoke($null, [object[]]@("$d\picture.png", 96, 1L)); [void]$t1.Wait(10000)
    Check 'thumbnail cache: a loaded thumbnail is remembered' ((Cached "$d\picture.png" 96 1) -and $null -ne $t1.Result)
    $t2 = $task.Invoke($null, [object[]]@("$d\picture.png", 96, 1L))
    Check 'thumbnail cache: the second request is answered at once, with the same image' ($t2.IsCompleted -and [object]::ReferenceEquals($t1.Result, $t2.Result))
    Check 'thumbnail cache: a different update stamp (file changed) is not served from the old one' (-not (Cached "$d\picture.png" 96 2))
    Check 'thumbnail cache: a different size is a different entry' (-not (Cached "$d\picture.png" 160 1))
    $t3 = $task.Invoke($null, [object[]]@("$d\no_such.png", 96, 1L)); [void]$t3.Wait(10000)
    Check 'thumbnail cache: a failure is remembered too (no retry on every redraw)' ((Cached "$d\no_such.png" 96 1) -and $null -eq $t3.Result)

    # ── アイコンの大きさ(小・中・大)と、FileEntry の読み直し
    function Size-Of([string]$name) { [Enum]::Parse($IS, $name) }
    $VS.GetProperty('IconSize').SetValue($null, (Size-Of 'Medium'))
    Check 'icon size: medium = 80px image, 96px thumbnails' (($VS.GetProperty('IconImageSize').GetValue($null) -eq 80) -and ($TS.GetProperty('RequestSize').GetValue($null) -eq 96))
    $VS.GetProperty('IconSize').SetValue($null, (Size-Of 'Large'))
    Check 'icon size: large = 128px image, 160px thumbnails, wider item' (($VS.GetProperty('IconImageSize').GetValue($null) -eq 128) -and ($TS.GetProperty('RequestSize').GetValue($null) -eq 160) -and ($VS.GetProperty('IconItemWidth').GetValue($null) -eq 156))
    $VS.GetProperty('IconSize').SetValue($null, (Size-Of 'Small'))
    Check 'icon size: small = 48px image, 64px thumbnails' (($VS.GetProperty('IconImageSize').GetValue($null) -eq 48) -and ($TS.GetProperty('RequestSize').GetValue($null) -eq 64))
    $VS.GetProperty('IconSize').SetValue($null, (Size-Of 'Medium'))

    $entry = [System.Activator]::CreateInstance($FE, [object[]]@([System.IO.FileInfo]::new("$d\picture.png"), $null))
    $first = $entry.Thumbnail
    $ok = $false
    for ($i = 0; $i -lt 100 -and -not $ok; $i++) { Start-Sleep -Milliseconds 100; $ok = $entry.Thumbnail.PixelWidth -ge 32 }
    Check 'FileEntry: the thumbnail is loaded at the medium size' $ok
    $mediumImage = $entry.Thumbnail
    $VS.GetProperty('IconSize').SetValue($null, (Size-Of 'Large'))
    $second = $entry.Thumbnail
    $reloaded = $false
    for ($i = 0; $i -lt 100 -and -not $reloaded; $i++) { Start-Sleep -Milliseconds 100; $reloaded = -not [object]::ReferenceEquals($entry.Thumbnail, $mediumImage) }
    Check 'FileEntry: after the icon size changes, the thumbnail is loaded again at the new size' $reloaded
    $VS.GetProperty('IconSize').SetValue($null, (Size-Of 'Medium'))
    $again = $entry.Thumbnail
    Check 'FileEntry: going back to a size that was already loaded is answered from the cache at once (the same image as before)' ([object]::ReferenceEquals($again, $mediumImage))
}
finally {
    Remove-TestDir $d
}
Show-Result
