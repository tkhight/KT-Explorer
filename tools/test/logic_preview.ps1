# プレビューの内容の作成(PreviewLoader)のロジックの検証。画面は出さず、ビルドした DLL を直接呼ぶ。
# 文字コードの判定(UTF-8 / BOM つき / UTF-16 / Shift-JIS)、バイナリの判別、大きなテキストの切り詰め、画像、フォルダ。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_preview.ps1'
# 操作するフォルダは %TEMP%\kt_test_preview の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$PL = $asm.GetType('KT_Explorer.Services.PreviewLoader')
$none = [System.Threading.CancellationToken]::None
function Load-Preview([string]$path, [bool]$isDir = $false) { $PL.GetMethod('Load').Invoke($null, [object[]]@($path, $isDir, $none)) }

$d = New-TestDir 'kt_test_preview'
try {
    $jp = '日本語のテキスト'   # 日本語(文字コードの判定を確かめる)

    # ── テキスト: 文字コードごと
    [System.IO.File]::WriteAllText("$d\utf8.txt", "$jp`nline2", (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText("$d\utf8bom.txt", "$jp`nline2", (New-Object System.Text.UTF8Encoding($true)))
    [System.IO.File]::WriteAllText("$d\utf16.txt", "$jp`nline2", [System.Text.Encoding]::Unicode)
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
    [System.IO.File]::WriteAllBytes("$d\sjis.txt", [System.Text.Encoding]::GetEncoding(932).GetBytes("$jp`nline2"))
    foreach ($n in 'utf8', 'utf8bom', 'utf16', 'sjis') {
        $r = Load-Preview "$d\$n.txt"
        Check "text: $n is shown as text and decoded correctly" (("$($r.Kind)" -eq 'Text') -and $r.Text.StartsWith($jp) -and $r.Text.Contains('line2'))
    }
    [System.IO.File]::WriteAllBytes("$d\empty.txt", [byte[]]@())
    $r = Load-Preview "$d\empty.txt"
    Check 'text: an empty file is shown as (empty) text' (("$($r.Kind)" -eq 'Text') -and $r.Text -eq '')

    # ── 拡張子では分からないファイルは、中身を見て判断する(テキストなら表示、バイナリなら情報だけ)
    [System.IO.File]::WriteAllText("$d\noext", 'plain text without an extension')
    $r = Load-Preview "$d\noext"
    Check 'unknown extension: readable text is shown' (("$($r.Kind)" -eq 'Text') -and $r.Text.StartsWith('plain text'))
    $bin = New-Object byte[] 2000; for ($i = 0; $i -lt $bin.Length; $i++) { $bin[$i] = [byte]($i % 7) }   # 0 のバイトを含む
    [System.IO.File]::WriteAllBytes("$d\data.bin", $bin)
    $r = Load-Preview "$d\data.bin"
    Check 'binary: shows information only, not garbled text' (("$($r.Kind)" -eq 'Info') -and $r.Info.Contains('BIN ファイル') -and $null -eq $r.Text)

    # ── 大きなテキストは、先頭の 64 KB だけを表示して、その旨を示す(切り口の日本語が壊れない)
    $big = ('あ' * 100000)   # UTF-8 で 300 KB(1 文字 3 バイト。64 KB の境界が、文字の途中になりうる)
    [System.IO.File]::WriteAllText("$d\big.txt", $big, (New-Object System.Text.UTF8Encoding($false)))
    $r = Load-Preview "$d\big.txt"
    Check 'large text: shown as text, truncated with a note' (("$($r.Kind)" -eq 'Text') -and $r.Text.Contains('先頭の 64 KB'))
    Check 'large text: the cut in the middle of a character does not break it' (-not $r.Text.Contains([string][char]0xFFFD) -and $r.Text.StartsWith('あ'))

    # ── 画像(1x1 の PNG)
    [System.IO.File]::WriteAllBytes("$d\pixel.png", [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='))
    $r = Load-Preview "$d\pixel.png"
    Check 'image: loaded as an image' (("$($r.Kind)" -eq 'Image') -and $null -ne $r.Image -and $r.Image.IsFrozen)
    Check 'image: size is shown (1 × 1 px)' ($r.Info.Contains('1 × 1 px'))
    [System.IO.File]::WriteAllBytes("$d\broken.png", [byte[]](1, 2, 3, 4, 5))
    $r = Load-Preview "$d\broken.png"
    Check 'image: a broken image gives a message instead of an error' (("$($r.Kind)" -eq 'Info') -and $r.Info.Contains('プレビューできません'))

    # ── フォルダ・見つからないファイル
    New-Item -ItemType Directory "$d\folder\sub1", "$d\folder\sub2" | Out-Null; Set-Content -LiteralPath "$d\folder\f.txt" -Value 'x'
    $r = Load-Preview "$d\folder" $true
    Check 'folder: shows the number of folders and files' (("$($r.Kind)" -eq 'Info') -and $r.Info.Contains('2 個のフォルダー、1 個のファイル'))
    $r = Load-Preview "$d\missing.txt"
    Check 'a file that no longer exists: a message' (("$($r.Kind)" -eq 'Info') -and $r.Info.Contains('見つかりません'))

    # ── 取り消し
    $cts = New-Object System.Threading.CancellationTokenSource; $cts.Cancel()
    $cancelled = $false
    try { [void]$PL.GetMethod('Load').Invoke($null, [object[]]@("$d\big.txt", $false, $cts.Token)) } catch { $cancelled = $_.Exception.InnerException -is [System.OperationCanceledException] }
    Check 'a cancelled load stops' $cancelled
}
finally {
    Remove-TestDir $d
}
Show-Result
