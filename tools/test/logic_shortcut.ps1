# ショートカット(.lnk)のリンク先の解決(ShortcutService)と、タブの「開く」のロジックの検証。画面は出さない。
# リンク先がフォルダなら、その一覧のまま移動する(エクスプローラーを開かない)。ファイルへのリンクや、リンク先が無いものは、フォルダとしては扱わない。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_shortcut.ps1'
# 操作するフォルダは %TEMP%\kt_test_shortcut の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$SS = $asm.GetType('KT_Explorer.Services.ShortcutService')
$FE = $asm.GetType('KT_Explorer.Models.FileEntry')
$FT = $asm.GetType('KT_Explorer.ViewModels.FolderTabViewModel')

$d = New-TestDir 'kt_test_shortcut'
function New-Shortcut([string]$lnk, [string]$target) {
    $wsh = New-Object -ComObject WScript.Shell
    $sc = $wsh.CreateShortcut($lnk); $sc.TargetPath = $target; $sc.Save()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc); [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($wsh)
}
function Resolve-Folder([string]$lnk) {
    $args_ = [object[]]@($lnk, $null)
    $ok = $SS.GetMethod('TryResolveFolder').Invoke($null, $args_)
    [pscustomobject]@{ Ok = $ok; Folder = $args_[1] }
}
try {
    New-Item -ItemType Directory "$d\target folder\inner" | Out-Null
    Set-Content -LiteralPath "$d\target folder\file.txt" -Value 'x'
    New-Shortcut "$d\to_folder.lnk" "$d\target folder"
    New-Shortcut "$d\to_file.lnk" "$d\target folder\file.txt"
    New-Shortcut "$d\to_missing.lnk" "$d\no_such_folder"
    New-Item -ItemType Directory "$d\gone" | Out-Null
    New-Shortcut "$d\to_gone.lnk" "$d\gone"; Remove-Item -LiteralPath "$d\gone" -Force -Confirm:$false   # 作ったあとで、リンク先が消えた
    Set-Content -LiteralPath "$d\fake.lnk" -Value 'this is not a shortcut file'
    Set-Content -LiteralPath "$d\plain.txt" -Value 'x'

    Check 'IsShortcut: .lnk (any case) is a shortcut, other files are not' (
        $SS.GetMethod('IsShortcut').Invoke($null, @("$d\to_folder.lnk")) -and $SS.GetMethod('IsShortcut').Invoke($null, @("$d\X.LNK")) -and
        -not $SS.GetMethod('IsShortcut').Invoke($null, @("$d\plain.txt")))

    $r = Resolve-Folder "$d\to_folder.lnk"
    Check 'a shortcut to a folder resolves to that folder' ($r.Ok -and $r.Folder -eq "$d\target folder")
    $r = Resolve-Folder "$d\to_file.lnk"
    Check 'a shortcut to a file is not a folder' (-not $r.Ok)
    $r = Resolve-Folder "$d\to_missing.lnk"
    Check 'a shortcut to a folder that does not exist is not a folder' (-not $r.Ok)
    $r = Resolve-Folder "$d\to_gone.lnk"
    Check 'a shortcut whose folder was deleted afterwards is not a folder' (-not $r.Ok)
    $r = Resolve-Folder "$d\fake.lnk"
    Check 'a broken .lnk file is not a folder (and does not throw)' (-not $r.Ok)
    $r = Resolve-Folder "$d\plain.txt"
    Check 'a file that is not a shortcut is not a folder' (-not $r.Ok)
    $r = Resolve-Folder "$d\does_not_exist.lnk"
    Check 'a shortcut path that does not exist is not a folder' (-not $r.Ok)

    # ── タブの「開く」: リンク先がフォルダなら、そのタブが、リンク先へ移動する
    $tab = [Activator]::CreateInstance($FT, [object[]]@([string]$d))
    $entry = [System.Activator]::CreateInstance($FE, [object[]]@([System.IO.FileInfo]::new("$d\to_folder.lnk"), $null))
    $tab.Open($entry)
    Check 'Open on a shortcut to a folder: the same tab moves to the target folder' ($tab.Path -eq "$d\target folder")
    Check 'Open on a shortcut to a folder: Back returns to where it was' ($tab.CanGoBack)
    $tab.Dispose()
}
finally {
    Remove-TestDir $d
}
Show-Result
