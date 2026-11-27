# 配布用の zip を作る(.NET 8 の実行環境を同梱した、exe 1 つの形)。出力は dist\KT_Explorer-<版番号>-win-x64.zip。
# 版番号は src/KT_Explorer/KT_Explorer.csproj の <Version>。GitHub の Releases への掲載は、手作業。
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\release\publish.ps1'
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $root 'src\KT_Explorer\KT_Explorer.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if (-not $version) { throw 'Version is not set in the project file' }

$name = "KT_Explorer-$version-win-x64"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
$zip = Join-Path $dist "$name.zip"
# 消すのは、このスクリプトが作る 2 つだけ
foreach ($p in $stage, $zip) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force } }

dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# 同梱する文書(使い方.html と images は、プロジェクトの設定で、exe の隣に出る): このアプリのライセンス、同梱しているもの(.NET、CommunityToolkit.Mvvm)のライセンス
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
if (-not (Test-Path -LiteralPath (Join-Path $stage '使い方.html'))) { throw 'the usage page was not published next to the exe' }
$third = New-Item -ItemType Directory (Join-Path $stage 'third-party')
$dotnetDir = Split-Path (Get-Command dotnet).Source
$toolkit = Join-Path $env:USERPROFILE '.nuget\packages\communitytoolkit.mvvm'
$toolkitVersion = (([xml](Get-Content -LiteralPath $project -Raw)).Project.ItemGroup.PackageReference | Where-Object Include -eq 'CommunityToolkit.Mvvm').Version
Copy-Item -LiteralPath (Join-Path $dotnetDir 'LICENSE.txt') -Destination (Join-Path $third 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $dotnetDir 'ThirdPartyNotices.txt') -Destination (Join-Path $third 'dotnet-ThirdPartyNotices.txt')
Copy-Item -LiteralPath (Join-Path $toolkit "$toolkitVersion\License.md") -Destination (Join-Path $third 'CommunityToolkit.Mvvm-License.md')
Copy-Item -LiteralPath (Join-Path $toolkit "$toolkitVersion\ThirdPartyNotices.txt") -Destination (Join-Path $third 'CommunityToolkit.Mvvm-ThirdPartyNotices.txt')

Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object { '{0,12:N0}  {1}' -f $_.Length, $_.FullName.Substring($stage.Length + 1) }
'zip: {0} ({1:N1} MB)' -f $zip, ((Get-Item -LiteralPath $zip).Length / 1MB)
'SHA256: ' + (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
