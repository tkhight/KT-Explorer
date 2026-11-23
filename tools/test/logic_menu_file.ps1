# メニューバー「ファイル」のロジック(WinMerge の引数の組み立て・見つけ方)の検証。画面は出さず、WinMerge も起動しない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_menu_file.ps1'
# 操作するフォルダは %TEMP%\kt_test_menu_file の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$WM = $asm.GetType('KT_Explorer.Services.WinMergeService')

function Build([string[]]$paths) {
    $r = $WM.GetMethod('BuildArguments').Invoke($null, [object[]]@(, [System.Collections.Generic.List[string]]$paths))
    if ($null -eq $r) { return $null }
    , @($r | ForEach-Object { $_ })
}

$d = New-TestDir 'kt_test_menu_file'
try {
    New-Item -ItemType Directory "$d\dirA", "$d\dirB" | Out-Null
    Set-Content -LiteralPath "$d\a.txt" -Value 'x'; Set-Content -LiteralPath "$d\b.txt" -Value 'y'

    $r = Build @()
    Check 'args: nothing selected -> just start WinMerge (no arguments)' (($null -ne $r) -and $r.Count -eq 0)
    $r = Build @("$d\a.txt", "$d\b.txt")
    Check 'args: two files -> compare them (/e then the two paths)' (($r.Count -eq 3) -and ($r[0] -eq '/e') -and ($r[1] -eq "$d\a.txt") -and ($r[2] -eq "$d\b.txt"))
    $r = Build @("$d\dirA", "$d\dirB")
    Check 'args: two folders -> compare them' (($r.Count -eq 3) -and ($r[1] -eq "$d\dirA"))
    Check 'args: a file and a folder cannot be compared together' ($null -eq (Build @("$d\a.txt", "$d\dirA")))
    Check 'args: more than two -> not accepted' ($null -eq (Build @("$d\a.txt", "$d\b.txt", "$d\dirA")))
    $r = Build @("$d\a.txt")
    Check 'args: one path -> opened as the left side' (($r.Count -eq 2) -and ($r[1] -eq "$d\a.txt"))

    $exe = $WM.GetMethod('FindExecutable').Invoke($null, @())
    Check 'find: the result is null or an existing WinMergeU.exe' (($null -eq $exe) -or (Test-Path -LiteralPath $exe -PathType Leaf))
}
finally {
    Remove-TestDir $d
}
Show-Result
