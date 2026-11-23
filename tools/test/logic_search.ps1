# 検索(再帰検索の絞り込み条件と、ファイルの中身の検索)のロジックの検証。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_search.ps1'
# 操作するフォルダは %TEMP%\kt_test_search の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$CS = $asm.GetType('KT_Explorer.Services.ContentSearch')
$FT = $asm.GetType('KT_Explorer.ViewModels.FolderTabViewModel')

function Has-Content([string]$path, [string]$term) { [bool]$CS.GetMethod('FileContains').Invoke($null, [object[]]@($path, $term, [System.Threading.CancellationToken]::None)) }

# 検索(SearchCore)を直接実行して、結果の名前の一覧を返す(タブの一覧は画面のスレッドが要るため、使わない)
Add-Type -IgnoreWarnings -ReferencedAssemblies $global:Kt.Dll, 'System.Collections', 'System.Runtime', 'WindowsBase' -TypeDefinition @"
using System;
using System.Collections.Generic;
public class KtSearchProbe : IProgress<(List<KT_Explorer.Models.FileEntry> Batch, int Folders)> {
    public List<string> Names = new List<string>();
    public void Report((List<KT_Explorer.Models.FileEntry> Batch, int Folders) p) { foreach (var e in p.Batch) Names.Add(e.Name); }
}
"@
$FQ = $asm.GetType('KT_Explorer.Services.FilterQuery')
function Run-Search([string]$root, [string]$text) {
    $q = $FQ.GetMethod('Parse').Invoke($null, [object[]]@($text, [datetime]::Now, $true))
    $probe = New-Object KtSearchProbe
    $flags = [Reflection.BindingFlags]'NonPublic,Public,Static'
    $m = $FT.GetMethod('SearchCore', $flags)
    if ($q.IsEmpty) { return [pscustomobject]@{ Names = @(); Status = ($q.Problems -join ' / ') } }
    [void]$m.Invoke($null, [object[]]@([string]$root, $q.psobject.BaseObject, $probe.psobject.BaseObject, [System.Threading.CancellationToken]::None))
    # 検索結果の名前は、検索元からの相対パスなので、末尾の名前だけにする
    [pscustomobject]@{ Names = @($probe.Names | ForEach-Object { Split-Path $_ -Leaf } | Sort-Object); Status = ($q.Problems -join ' / ') }
}

$d = New-TestDir 'kt_test_search'
try {
    New-Item -ItemType Directory "$d\docs", "$d\docs\deep", "$d\pics" | Out-Null
    [System.IO.File]::WriteAllText("$d\docs\memo.txt", 'Hello World project plan', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText("$d\docs\deep\notes.md", 'second file: world peace', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText("$d\docs\sjis.txt", 'ラベル検索テスト', [System.Text.Encoding]::GetEncoding(932))
    [System.IO.File]::WriteAllText("$d\docs\bom.txt", '日本語の本文', [System.Text.UTF8Encoding]::new($true))
    [System.IO.File]::WriteAllBytes("$d\pics\a.png", [byte[]](0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0x57, 0x6F, 0x72, 0x6C, 0x64))
    [System.IO.File]::WriteAllText("$d\pics\b.txt", 'unrelated', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllBytes("$d\big.txt", (New-Object byte[] (3MB)))

    # ── 中身の検索(ContentSearch)
    Check 'content: a word inside a text file is found (case-insensitive)' (Has-Content "$d\docs\memo.txt" 'WORLD')
    Check 'content: a word that is not there is not found' (-not (Has-Content "$d\docs\memo.txt" 'absent'))
    Check 'content: a Shift-JIS file is found' (Has-Content "$d\docs\sjis.txt" 'ラベル')
    Check 'content: a UTF-8 (BOM) Japanese file is found' (Has-Content "$d\docs\bom.txt" '本文')
    Check 'content: a binary file is skipped (even if the bytes spell the word)' (-not (Has-Content "$d\pics\a.png" 'World'))
    Check 'content: a missing file is not an error' (-not (Has-Content "$d\nope.txt" 'x'))

    # ── 検索(名前の条件。以前と同じ使い方が、そのまま動く)
    $r = Run-Search $d 'memo'
    Check 'search: a plain word is a name match, including subfolders' (($r.Names.Count -eq 1) -and ($r.Names[0] -eq 'memo.txt'))
    $r = Run-Search $d '*.txt'
    Check 'search: a wildcard matches the whole name' (($r.Names -contains 'memo.txt') -and ($r.Names -contains 'b.txt') -and (-not ($r.Names -contains 'notes.md')))

    # ── 検索で、絞り込みの条件が使える
    $r = Run-Search $d 'type:画像'
    Check 'search: type: works in a search' (($r.Names.Count -eq 1) -and ($r.Names[0] -eq 'a.png'))
    $r = Run-Search $d 'ext:md'
    Check 'search: ext: works in a search (subfolder file)' (($r.Names.Count -eq 1) -and ($r.Names[0] -eq 'notes.md'))
    $r = Run-Search $d 'size:>2MB'
    Check 'search: size: works in a search' (($r.Names.Count -eq 1) -and ($r.Names[0] -eq 'big.txt'))
    $r = Run-Search $d 'type:フォルダー'
    Check 'search: type:folder lists the folders (all depths)' (($r.Names -contains 'docs') -and ($r.Names -contains 'deep') -and ($r.Names -contains 'pics'))
    $r = Run-Search $d 'ext:txt -memo'
    Check 'search: a negated name condition' (($r.Names -contains 'b.txt') -and (-not ($r.Names -contains 'memo.txt')))

    # ── 中身の条件(検索)
    $r = Run-Search $d 'content:world'
    Check 'search: content: finds text files containing the word (memo.txt, notes.md; not the binary png)' (($r.Names.Count -eq 2) -and ($r.Names -contains 'memo.txt') -and ($r.Names -contains 'notes.md'))
    $r = Run-Search $d '中身:ラベル'
    Check 'search: 中身: works, with Japanese text' (($r.Names.Count -eq 1) -and ($r.Names[0] -eq 'sjis.txt'))
    $r = Run-Search $d 'ext:md content:world'
    Check 'search: content: combines with other conditions (AND)' (($r.Names.Count -eq 1) -and ($r.Names[0] -eq 'notes.md'))
    $r = Run-Search $d 'ext:txt -content:world'
    Check 'search: -content: leaves out files with the word' ((-not ($r.Names -contains 'memo.txt')) -and ($r.Names -contains 'b.txt'))
    $r = Run-Search $d 'content:'
    Check 'search: an empty content: is reported, not run as "everything"' (($r.Names.Count -eq 0) -and ($r.Status -like '*解釈*'))

    # ── 絞込欄では、中身の条件は使えない(読み込みが重いため)
    $q = $FQ.GetMethod('Parse').Invoke($null, [object[]]@('content:world', [datetime]::Now, $false))
    Check 'filter box: content: is not applied (reported as a problem) and nothing is hidden' (($q.Problems.Count -eq 1) -and $q.IsEmpty)
}
finally {
    Remove-TestDir $d
}
Show-Result
