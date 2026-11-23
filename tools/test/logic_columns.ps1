# 詳細表示の追加の列(作成日時・属性・画像のサイズ・再生時間)と、列の選択のロジックの検証。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_columns.ps1'
# 操作するフォルダは %TEMP%\kt_test_columns の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$MSV = $asm.GetType('KT_Explorer.Services.MediaInfoService')
$MI = $asm.GetType('KT_Explorer.Services.MediaInfo')
$VS = $asm.GetType('KT_Explorer.Services.ViewSettings')
$FE = $asm.GetType('KT_Explorer.Models.FileEntry')

function Media-Of([string]$path) {
    $m = $MSV.GetMethods() | Where-Object { $_.Name -eq 'GetAsync' } | Select-Object -First 1
    $t = $m.Invoke($null, [object[]]@($path, 1L)); [void]$t.Wait(15000); $t.Result
}
function New-Entry([string]$path) {
    $info = if (Test-Path -LiteralPath $path -PathType Container) { [System.IO.DirectoryInfo]::new($path) } else { [System.IO.FileInfo]::new($path) }
    [System.Activator]::CreateInstance($FE, [object[]]@($info, $null))
}

$d = New-TestDir 'kt_test_columns'
try {
    # 画像(64x48 の PNG)
    $pixels = New-Object byte[] (64 * 48 * 4)
    $bmp = [System.Windows.Media.Imaging.BitmapSource]::Create(64, 48, 96, 96, [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, 256)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [System.IO.File]::Create("$d\pic.png"); $enc.Save($fs); $fs.Dispose()

    # 音声(2 秒の無音の WAV: 8kHz・8bit・モノラル)
    $rate = 8000; $data = New-Object byte[] ($rate * 2); for ($i = 0; $i -lt $data.Length; $i++) { $data[$i] = 128 }
    $wav = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter($wav)
    $bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $data.Length)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$rate); $bw.Write([int]$rate); $bw.Write([int16]1); $bw.Write([int16]8)
    $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]$data.Length); $bw.Write($data); $bw.Flush()
    [System.IO.File]::WriteAllBytes("$d\tone.wav", $wav.ToArray())
    [System.IO.File]::WriteAllText("$d\plain.txt", 'x')

    # ── 画像のサイズ・再生時間
    $m = Media-Of "$d\pic.png"
    Check 'media: image size is read (64 x 48)' (($m.Width -eq 64) -and ($m.Height -eq 48) -and ($m.DimensionsText -eq '64 × 48'))
    $m = Media-Of "$d\tone.wav"
    Check 'media: audio duration is read (about 2 seconds -> 0:02)' (($m.DurationText -eq '0:02'))
    $m = Media-Of "$d\plain.txt"
    Check 'media: a text file has neither size nor duration' (($m.DimensionsText -eq '') -and ($m.DurationText -eq ''))
    $m = Media-Of "$d\missing.png"
    Check 'media: a missing file does not fail' (($m.DimensionsText -eq '') -and ($m.DurationText -eq ''))
    $fmt = $MI.GetMethod('FormatDuration')
    Check 'duration text: m:ss and h:mm:ss' (($fmt.Invoke($null, [object[]]@([TimeSpan]::FromSeconds(205))) -eq '3:25') -and ($fmt.Invoke($null, [object[]]@([TimeSpan]::FromSeconds(3723))) -eq '1:02:03'))
    Check 'candidates: only images / video / audio are looked up' (($MSV.GetMethod('IsCandidate').Invoke($null, [object[]]@('a.JPG'))) -and (-not $MSV.GetMethod('IsCandidate').Invoke($null, [object[]]@('a.txt'))))

    # ── FileEntry の列の値
    $e = New-Entry "$d\pic.png"
    Check 'entry: created date text' ($e.CreatedText -eq ([System.IO.File]::GetCreationTime("$d\pic.png")).ToString('yyyy/MM/dd HH:mm'))
    $first = $e.ImageSizeText
    $ok = $false; for ($i = 0; $i -lt 100 -and -not $ok; $i++) { Start-Sleep -Milliseconds 100; $ok = ($e.ImageSizeText -eq '64 × 48') }
    Check 'entry: image size is loaded in the background after the first read' $ok
    Check 'entry: a text file has an empty image size and duration' ((New-Entry "$d\plain.txt").ImageSizeText -eq '' -and (New-Entry "$d\plain.txt").DurationText -eq '')
    $f = New-Entry "$d\plain.txt"
    (Get-Item "$d\plain.txt").Attributes = 'ReadOnly,Archive'
    $f = New-Entry "$d\plain.txt"
    Check 'entry: attributes as letters (read-only + archive = "R A")' ($f.AttributesText -eq 'R A')
    (Get-Item "$d\plain.txt").Attributes = 'Normal'
    Check 'entry: a folder shows no media info' ((New-Entry $d).ImageSizeText -eq '')

    # ── 列の選択
    $VS.GetMethod('LoadColumns').Invoke($null, [object[]]@($null))
    Check 'columns: none at first' ($VS.GetProperty('ExtraColumns').GetValue($null).Count -eq 0)
    $VS.GetMethod('SetColumnVisible').Invoke($null, [object[]]@('duration', $true))
    $VS.GetMethod('SetColumnVisible').Invoke($null, [object[]]@('created', $true))
    $cols = @($VS.GetProperty('ExtraColumns').GetValue($null))
    Check 'columns: shown in the catalog order, not the order they were switched on' (($cols.Count -eq 2) -and ($cols[0] -eq 'created') -and ($cols[1] -eq 'duration'))
    $VS.GetMethod('SetColumnVisible').Invoke($null, [object[]]@('nonsense', $true))
    Check 'columns: an unknown key is ignored' ($VS.GetProperty('ExtraColumns').GetValue($null).Count -eq 2)
    $VS.GetMethod('SetColumnVisible').Invoke($null, [object[]]@('created', $false))
    Check 'columns: switching one off' ((@($VS.GetProperty('ExtraColumns').GetValue($null)) -join ',') -eq 'duration')
    $VS.GetMethod('LoadColumns').Invoke($null, [object[]]@(,[string[]]@('attributes', 'bogus', 'dimensions')))
    Check 'columns: loading a saved list keeps only known keys' ((@($VS.GetProperty('ExtraColumns').GetValue($null)) -join ',') -eq 'attributes,dimensions')
    $VS.GetMethod('LoadColumns').Invoke($null, [object[]]@($null))
}
finally {
    Remove-TestDir $d
}
Show-Result
