param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path $PSScriptRoot '..' | Resolve-Path
$project = Join-Path $repoRoot 'src/DotNetPythonEmbed/DotNetPythonEmbed.csproj'
$outputDir = Join-Path $repoRoot 'artifacts'

if (-not $env:NUGET_API_KEY) {
    throw 'NUGET_API_KEY environment variable must be set.'
}

if (Test-Path $outputDir) {
    Remove-Item $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDir | Out-Null

$restoreArgs = @(
    'restore'
    (Join-Path $repoRoot 'DotNetPythonEmbed.sln')
)

dotnet @restoreArgs

dotnet build (Join-Path $repoRoot 'DotNetPythonEmbed.sln') --configuration Release --no-restore

$packArgs = @(
    'pack'
    $project
    '--configuration' 'Release'
    '--output' $outputDir
    '--no-build'
)

if ($Version) {
    $packArgs += "/p:PackageVersion=$Version"
}

dotnet @packArgs

$packagePath = Get-ChildItem -Path $outputDir -Filter 'DotNetPythonEmbed*.nupkg' | Sort-Object FullName | Select-Object -Last 1
if (-not $packagePath) {
    throw "Failed to locate packed nupkg in $outputDir"
}

$pushArgs = @(
    'nuget'
    'push'
    $packagePath.FullName
    '--source' 'https://api.nuget.org/v3/index.json'
    '--api-key' $env:NUGET_API_KEY
    '--skip-duplicate'
)

dotnet @pushArgs

Write-Host "Published $($packagePath.FullName) to NuGet"
