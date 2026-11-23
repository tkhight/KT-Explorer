# 「絞込」欄の条件(FilterQuery)のロジックの検証。画面は出さず、ビルドした DLL を直接呼ぶ。
# 名前・ワイルドカード・引用符・除外(-)・type: / ext: / size: / date: と、解釈できない条件の扱い。
# 事前に: dotnet build src/KT_Explorer/KT_Explorer.csproj -o "$TEMP/kt_build_check"
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\logic_filter.ps1'
# 操作するフォルダは %TEMP%\kt_test_filter の中だけ。
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-Path -LiteralPath $global:Kt.Dll)) { Write-Output "ABORT: build not found: $($global:Kt.Dll)"; exit 2 }
Add-Type -AssemblyName PresentationFramework, WindowsBase, PresentationCore
$asm = [Reflection.Assembly]::LoadFrom($global:Kt.Dll)
$FQ = $asm.GetType('KT_Explorer.Services.FilterQuery')
$FE = $asm.GetType('KT_Explorer.Models.FileEntry')

# 日付の条件を確かめるため、「いま」を固定する(2024-06-15 は土曜日。この週の月曜日は 06-10)
$now = [datetime]'2024-06-15 12:00:00'

$d = New-TestDir 'kt_test_filter'
function New-File([string]$name, [int]$bytes, [datetime]$modified) {
    $p = Join-Path $d $name
    [System.IO.File]::WriteAllBytes($p, (New-Object byte[] $bytes))
    (Get-Item -LiteralPath $p).LastWriteTime = $modified
}
try {
    New-File 'a.png'              100            ([datetime]'2024-05-10 10:00')
    New-File 'b.jpg'              (5MB)          ([datetime]'2024-06-01 10:00')
    New-File 'c.txt'              50             ([datetime]'2024-06-15 09:00')   # 今日
    New-File 'd.zip'              2048           ([datetime]'2024-06-14 10:00')   # 昨日
    New-File 'report_final.docx'  1024           ([datetime]'2023-12-31 10:00')
    New-File 'notes draft.md'     10             ([datetime]'2024-06-12 08:00')   # 3日前(今週)
    New-Item -ItemType Directory (Join-Path $d 'sub') | Out-Null
    (Get-Item -LiteralPath (Join-Path $d 'sub')).LastWriteTime = [datetime]'2024-06-13 10:00'

    $entries = @(Get-ChildItem -LiteralPath $d -Force | ForEach-Object {
        [System.Activator]::CreateInstance($FE, [object[]]@($_.PSObject.BaseObject, $null)) })
    function Names([string]$query) {
        $q = $FQ.GetMethod('Parse').Invoke($null, [object[]]@($query, $now, $false))
        (@($entries | Where-Object { $q.Matches($_) } | ForEach-Object { $_.Name } | Sort-Object) -join ',')
    }
    function Problems([string]$query) { $q = $FQ.GetMethod('Parse').Invoke($null, [object[]]@($query, $now, $false)); @($q.Problems) }

    # ── 名前
    Check 'empty query: everything'                   ((Names '') -eq 'a.png,b.jpg,c.txt,d.zip,notes draft.md,report_final.docx,sub')
    Check 'plain text: contained in the name (case-insensitive)' ((Names 'REPORT') -eq 'report_final.docx')
    Check 'wildcard: *.png'                           ((Names '*.png') -eq 'a.png')
    Check 'wildcard: ?.txt'                           ((Names '?.txt') -eq 'c.txt')
    Check 'wildcard must match the whole name'        ((Names 'a*') -eq 'a.png')
    Check 'quoted text with a space'                  ((Names '"notes draft"') -eq 'notes draft.md')
    Check 'two terms: all must match (AND)'           ((Names 'report final') -eq 'report_final.docx')
    Check 'exclude with -'                            ((Names '-draft -sub -report') -eq 'a.png,b.jpg,c.txt,d.zip')

    # ── 種類・拡張子
    Check 'type:画像'                                 ((Names 'type:画像') -eq 'a.png,b.jpg')
    Check 'type:image (English)'                      ((Names 'type:image') -eq 'a.png,b.jpg')
    Check 'type:フォルダー'                           ((Names 'type:フォルダー') -eq 'sub')
    Check 'type:ファイル excludes folders'            ((Names 'type:ファイル -type:画像') -eq 'c.txt,d.zip,notes draft.md,report_final.docx')
    Check 'type:圧縮'                                 ((Names 'type:圧縮') -eq 'd.zip')
    Check 'ext:png,jpg'                               ((Names 'ext:png,jpg') -eq 'a.png,b.jpg')
    Check 'ext:.txt (with a dot)'                     ((Names 'ext:.txt') -eq 'c.txt')

    # ── サイズ(フォルダーは、サイズを計算していないと、サイズの条件に合わない)
    Check 'size:>1MB'                                 ((Names 'size:>1MB') -eq 'b.jpg')
    Check 'size:<=100'                                ((Names 'size:<=100') -eq 'a.png,c.txt,notes draft.md')
    Check 'size:1KB..3KB'                             ((Names 'size:1KB..3KB') -eq 'd.zip,report_final.docx')
    Check 'size:2KB (equal)'                          ((Names 'size:2KB') -eq 'd.zip')
    Check 'size:>=50 -type:画像 (combined)'           ((Names 'size:>=50 -type:画像') -eq 'c.txt,d.zip,report_final.docx')

    # ── 更新日時
    Check 'date:今日'                                 ((Names 'date:今日') -eq 'c.txt')
    Check 'date:昨日'                                 ((Names 'date:昨日') -eq 'd.zip')
    Check 'date:今週 (Monday to now)'                 ((Names 'date:今週') -eq 'c.txt,d.zip,notes draft.md,sub')
    Check 'date:今月'                                 ((Names 'date:今月') -eq 'b.jpg,c.txt,d.zip,notes draft.md,sub')
    Check 'date:今年'                                 ((Names 'date:今年') -eq 'a.png,b.jpg,c.txt,d.zip,notes draft.md,sub')
    Check 'date:7d (within 7 days)'                   ((Names 'date:7d') -eq 'c.txt,d.zip,notes draft.md,sub')
    Check 'date:>2024-06-01'                          ((Names 'date:>2024-06-01') -eq 'c.txt,d.zip,notes draft.md,sub')
    Check 'date:<2024-01-01'                          ((Names 'date:<2024-01-01') -eq 'report_final.docx')
    Check 'date:2024-05-10 (that day)'                ((Names 'date:2024-05-10') -eq 'a.png')
    Check 'date:2024-06-12..2024-06-14 (range)'       ((Names 'date:2024-06-12..2024-06-14') -eq 'd.zip,notes draft.md,sub')

    # ── 解釈できない条件は、無視して、理由を返す(全部を隠してしまわない)
    Check 'bad size: ignored, listed as a problem'    (((Names 'size:abc') -eq 'a.png,b.jpg,c.txt,d.zip,notes draft.md,report_final.docx,sub') -and (Problems 'size:abc').Count -eq 1)
    Check 'bad type: ignored, listed as a problem'    (((Names 'type:xyz png') -eq 'a.png') -and (Problems 'type:xyz png').Count -eq 1)
    Check 'bad date: listed as a problem'             ((Problems 'date:来週').Count -eq 1)
    Check 'a colon that is not a condition stays a name' ((Problems 'foo:bar').Count -eq 0)
    Check 'a lone "-" is ignored'                     ((Names '-') -eq 'a.png,b.jpg,c.txt,d.zip,notes draft.md,report_final.docx,sub')
}
finally {
    Remove-TestDir $d
}
Show-Result
