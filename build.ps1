param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectDir 'AlphaBleedFixer.csproj'
$outputDir = Join-Path $projectDir 'dist'
$archivePath = Join-Path $projectDir 'AlphaBleedFixer.zip'

dotnet build $projectFile -c $Configuration

if (Test-Path -LiteralPath $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDir | Out-Null

$buildDir = Join-Path $projectDir ("bin\{0}\net48" -f $Configuration)
Copy-Item -LiteralPath (Join-Path $buildDir 'AlphaBleedFixer.exe') -Destination $outputDir
if (Test-Path -LiteralPath (Join-Path $buildDir 'AlphaBleedFixer.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $buildDir 'AlphaBleedFixer.exe.config') -Destination $outputDir
}
Copy-Item -LiteralPath (Join-Path $projectDir 'AlphaBleedFixer.ini') -Destination $outputDir

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $outputDir '*') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "Built: $outputDir\AlphaBleedFixer.exe"
Write-Host "Packaged: $archivePath"
