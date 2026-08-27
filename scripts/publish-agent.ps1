[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [switch]$FrameworkDependent,

    [string]$OutputDirectory = 'artifacts\Xugar.Endpoint.Agent'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishOutput = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
if (-not $publishOutput.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must remain under the repository artifacts directory.'
}

$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw ".NET SDK executable was not found at $dotnetPath"
}

$projectPath = Join-Path $repositoryRoot 'src\Xugar.Endpoint.Agent\Xugar.Endpoint.Agent.csproj'
New-Item -ItemType Directory -Path $publishOutput -Force | Out-Null

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $dotnetPath publish $projectPath `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    --output $publishOutput `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "Agent publish failed with exit code $LASTEXITCODE."
}

Write-Host "Xugar Endpoint Agent published to: $publishOutput"
Write-Host "Self-contained: $selfContained"
