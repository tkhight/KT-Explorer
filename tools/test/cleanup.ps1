# テストの途中で中断したときの後片付け: %TEMP%\kt_test_* のテスト用フォルダを消す。
# 使い方: & 'E:\KT_Applications\KT_Explorer\tools\test\cleanup.ps1'
# 消すのは、%TEMP% 直下の名前が kt_test_ で始まるフォルダだけ(common.ps1 の Remove-TestDir が範囲外を拒否する)。
. (Join-Path $PSScriptRoot 'common.ps1')

$targets = @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'kt_test_*' -ErrorAction SilentlyContinue)
foreach ($t in $targets) {
    Remove-TestDir $t.FullName
    Write-Output "removed: $($t.FullName)"
}
if ($targets.Count -eq 0) { Write-Output 'nothing to clean' }
