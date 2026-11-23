# 開いたフォルダの履歴(FolderHistory)のロジックの検証。画面は出さず、ビルドした DLL を直接呼ぶ。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_history.ps1'
# 操作するフォルダは %TEMP%\kt_test_history の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$FH = $asm.GetType('KT_Explorer.Services.FolderHistory')
$VST = $asm.GetType('KT_Explorer.Services.VisitState')
$FT = $asm.GetType('KT_Explorer.ViewModels.FolderTabViewModel')

function Rec([string]$p) { [void]$FH.GetMethod('Record').Invoke($null, [object[]]@($p)) }
function Recent([int]$n) { , @($FH.GetMethod('Recent').Invoke($null, [object[]]@($n)) | ForEach-Object { $_ }) }
function Frequent([int]$n, [int]$min) { , @($FH.GetMethod('Frequent').Invoke($null, [object[]]@($n, $min)) | ForEach-Object { $_ }) }

$d = New-TestDir 'kt_test_history'
try {
    $FH.GetMethod('Clear').Invoke($null, @())
    $clock = [pscustomobject]@{ Now = [datetime]'2026-01-01T10:00:00' }
    $FH.GetProperty('Now').SetValue($null, [Func[datetime]]{ $clock.Now }.GetNewClosure())

    Rec 'C:\a'; $clock.Now = $clock.Now.AddMinutes(1)
    Rec 'C:\b'; $clock.Now = $clock.Now.AddMinutes(1)
    Rec 'C:\c'; $clock.Now = $clock.Now.AddMinutes(1)
    $r = Recent 10
    Check 'recent: newest first' (($r.Count -eq 3) -and ($r[0].Path -eq 'C:\c') -and ($r[2].Path -eq 'C:\a'))
    Rec 'c:\A\'; $clock.Now = $clock.Now.AddMinutes(1)
    $r = Recent 10
    Check 'record: case and a trailing backslash do not make a new entry; count goes up and it moves to the top' (($r.Count -eq 3) -and ($r[0].Count -eq 2))
    Check 'recent: the count limits the list' ((Recent 2).Count -eq 2)

    Rec 'C:\b'; Rec 'C:\b'
    $f = Frequent 10 2
    Check 'frequent: most visited first (b=3, a=2), single visits left out' (($f.Count -eq 2) -and ($f[0].Path -eq 'C:\b') -and ($f[0].Count -eq 3) -and ($f[1].Count -eq 2))
    $clock.Now = $clock.Now.AddMinutes(1); Rec 'C:\'
    Check 'drive root keeps its backslash' ((Recent 1)[0].Path -eq 'C:\')

    $s = @($FH.GetMethod('Search').Invoke($null, [object[]]@('B', 10)) | ForEach-Object { $_ })
    Check 'search: part of a path, any case' (($s.Count -eq 1) -and ($s[0].Path -eq 'C:\b'))

    # 状態への保存と復元
    $state = $FH.GetMethod('ToState').Invoke($null, @())
    $before = (Recent 10).Count
    $FH.GetMethod('Clear').Invoke($null, @())
    Check 'clear: empties the history' ((Recent 10).Count -eq 0)
    [void]$FH.GetMethod('Load').Invoke($null, [object[]]@(, $state))
    $r = Recent 10
    Check 'state: saved and restored (entries, counts, order)' (($r.Count -eq $before) -and ((Frequent 10 2)[0].Path -eq 'C:\b'))

    # 上限(300 件)。古いものから捨てる
    $FH.GetMethod('Clear').Invoke($null, @())
    for ($i = 0; $i -lt 310; $i++) { $clock.Now = $clock.Now.AddSeconds(1); Rec "C:\many\f$i" }
    $r = Recent 400
    Check 'limit: 300 entries are kept, the oldest are dropped' (($r.Count -eq 300) -and ($r[0].Path -eq 'C:\many\f309') -and (-not ($r | Where-Object { $_.Path -eq 'C:\many\f0' })))
    $FH.GetProperty('Now').SetValue($null, [Func[datetime]]{ [datetime]::Now })

    # タブの移動が履歴に残る(作った時の場所は数えない)
    $FH.GetMethod('Clear').Invoke($null, @())
    New-Item -ItemType Directory "$d\x", "$d\y" | Out-Null
    $tab = [Activator]::CreateInstance($FT, [object[]]@([string]$d))
    Check 'tab: the place a tab starts in is not counted' ((Recent 10).Count -eq 0)
    $tab.NavigateTo("$d\x", $true); $tab.NavigateTo("$d\y", $true)
    $r = Recent 10
    Check 'tab: moving to a folder records it' (($r.Count -eq 2) -and ($r[0].Path -eq "$d\y"))
    $tab.GoBack()
    Check 'tab: going back counts as a visit too' (((Recent 1)[0].Path -eq "$d\x") -and ((Recent 1)[0].Count -eq 2))
    $tab.Dispose()
}
finally {
    $FH.GetMethod('Clear').Invoke($null, @())
    Remove-TestDir $d
}
Show-Result
