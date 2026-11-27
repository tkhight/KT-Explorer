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

    # 一覧の右クリックで比較する 2 つ
    function Targets([string[]]$mine, $other) {
        $o = $null
        if ($null -ne $other) { $o = [System.Collections.Generic.List[string]]::new(); foreach ($p in $other) { $o.Add($p) } }   # if の値にすると、要素に分解される
        $r = $WM.GetMethod('ContextTargets').Invoke($null, [object[]]@([System.Collections.Generic.List[string]]$mine, $o))
        if ($null -eq $r) { return 'none' }
        (@($r | ForEach-Object { Split-Path $_ -Leaf }) -join ',')
    }
    Check 'context: two selected -> those two' ((Targets @("$d\a.txt", "$d\b.txt") $null) -eq 'a.txt,b.txt')
    Check 'context: two selected wins over the other pane selection' ((Targets @("$d\a.txt", "$d\b.txt") @("$d\a.txt")) -eq 'a.txt,b.txt')
    Check 'context: one here and one in the next pane -> both, this pane first' ((Targets @("$d\b.txt") @("$d\a.txt")) -eq 'b.txt,a.txt')
    Check 'context: one selected without another pane -> no item' ((Targets @("$d\a.txt") $null) -eq 'none')
    Check 'context: the next pane has none or two selected -> no item' (((Targets @("$d\a.txt") @()) -eq 'none') -and ((Targets @("$d\a.txt") @("$d\a.txt", "$d\b.txt")) -eq 'none'))
    Check 'context: a file and a folder -> no item' (((Targets @("$d\a.txt", "$d\dirA") $null) -eq 'none') -and ((Targets @("$d\a.txt") @("$d\dirA")) -eq 'none'))
    Check 'context: three selected -> no item' ((Targets @("$d\a.txt", "$d\b.txt", "$d\dirA") $null) -eq 'none')

    $exe = $WM.GetMethod('FindExecutable').Invoke($null, @())
    Check 'find: the result is null or an existing WinMergeU.exe' (($null -eq $exe) -or (Test-Path -LiteralPath $exe -PathType Leaf))
}
finally {
    Remove-TestDir $d
}
Show-Result
