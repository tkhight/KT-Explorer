# パス欄の補助(PathBarService): 入力補完の候補と、パンくず(階層の分割)のロジックの検証。画面は出さない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_pathbar.ps1'
# 操作するフォルダは %TEMP%\kt_test_pathbar の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$PB = $asm.GetType('KT_Explorer.Services.PathBarService')
$FH = $asm.GetType('KT_Explorer.Services.FolderHistory')

function Suggest([string]$t) { , @($PB.GetMethod('Suggest').Invoke($null, [object[]]@($t, 12)) | ForEach-Object { $_ }) }
function Split-Path2([string]$p) { , @($PB.GetMethod('Split').Invoke($null, [object[]]@($p)) | ForEach-Object { $_ }) }

$d = New-TestDir 'kt_test_pathbar'
try {
    $FH.GetMethod('Clear').Invoke($null, @())
    New-Item -ItemType Directory "$d\alpha", "$d\alpine", "$d\beta", "$d\alpha\inner" | Out-Null
    [System.IO.File]::WriteAllText("$d\alfile.txt", 'x')

    $s = Suggest "$d\al"
    Check 'suggest: folders whose name starts with the typed text (files are left out)' (($s.Count -eq 2) -and ($s[0] -eq "$d\alpha") -and ($s[1] -eq "$d\alpine"))
    $s = Suggest "$d\AL"
    Check 'suggest: not case-sensitive' ($s.Count -eq 2)
    $s = Suggest "$d\"
    Check 'suggest: a trailing backslash lists the folders inside' (($s.Count -eq 3) -and ($s -contains "$d\beta"))
    Check 'suggest: nothing matches -> empty' ((Suggest "$d\zzz").Count -eq 0)
    Check 'suggest: empty text -> empty' ((Suggest '').Count -eq 0)
    Check 'suggest: a folder that does not exist -> empty' ((Suggest "$d\nope\x").Count -eq 0)
    Check 'suggest: the limit is respected' ($PB.GetMethod('Suggest').Invoke($null, [object[]]@("$d\", 2)).Count -eq 2)

    # 履歴から(区切りの無い文字)
    $FH.GetMethod('Record').Invoke($null, [object[]]@("$d\beta"))
    $s = Suggest 'kt_test_pathbar\be'
    Check 'suggest: text without a drive is matched against the history' (($s.Count -ge 1) -and ($s -contains "$d\beta"))
    # 履歴の候補と、フォルダの候補が重ならない
    $FH.GetMethod('Record').Invoke($null, [object[]]@("$d\alpha"))
    $s = Suggest "$d\al"
    Check 'suggest: a folder found in both the disk and the history appears once' ((@($s | Where-Object { $_ -eq "$d\alpha" })).Count -eq 1)

    # パンくず
    $p = Split-Path2 'C:\Users\me\docs'
    Check 'breadcrumb: C:\Users\me\docs -> 4 parts' (($p.Count -eq 4) -and ($p[0].Name -eq 'C:\') -and ($p[0].FullPath -eq 'C:\'))
    Check 'breadcrumb: each part carries the path up to it' (($p[1].FullPath -eq 'C:\Users') -and ($p[3].Name -eq 'docs') -and ($p[3].FullPath -eq 'C:\Users\me\docs'))
    $p = Split-Path2 'C:\'
    Check 'breadcrumb: a drive root is one part' (($p.Count -eq 1) -and ($p[0].FullPath -eq 'C:\'))
    $p = Split-Path2 'D:\a\'
    Check 'breadcrumb: a trailing backslash is ignored' (($p.Count -eq 2) -and ($p[1].FullPath -eq 'D:\a'))
    $p = Split-Path2 '\\server\share\x\y'
    Check 'breadcrumb: a network path starts at \\server\share' (($p.Count -eq 3) -and ($p[0].FullPath -eq '\\server\share') -and ($p[2].FullPath -eq '\\server\share\x\y'))
    Check 'breadcrumb: empty -> no parts' ((Split-Path2 '').Count -eq 0)

    $c = @($PB.GetMethod('Children').Invoke($null, [object[]]@([string]$d)) | ForEach-Object { $_ })
    Check 'children: the folders inside, sorted, without files' (($c.Count -eq 3) -and ($c[0].Name -eq 'alpha') -and ($c[2].Name -eq 'beta'))
    Check 'children: a path that does not exist -> empty' (@($PB.GetMethod('Children').Invoke($null, [object[]]@("$d\none")) | ForEach-Object { $_ }).Count -eq 0)
}
finally {
    $FH.GetMethod('Clear').Invoke($null, @())
    Remove-TestDir $d
}
Show-Result
