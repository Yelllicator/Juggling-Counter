[CmdletBinding()]
param(
    [string]$GameDir = $env:STS2_GAME_DIR,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw 'Specify -GameDir or set STS2_GAME_DIR to your Slay the Spire 2 installation directory.'
}
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$gameDll = Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'
if (-not (Test-Path -LiteralPath $gameDll -PathType Leaf)) {
    throw "Cannot find the game's sts2.dll in $GameDir"
}
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw '.NET SDK 9 or later is required.'
}

& $dotnetPath build (Join-Path $PSScriptRoot 'STS2Count.csproj') -c $Configuration "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

$packageDir = Join-Path $PSScriptRoot 'dist\STS2Count'
$manifest = Get-Content -LiteralPath (Join-Path $packageDir 'STS2Count.json') -Raw | ConvertFrom-Json
$packageFiles = @(Get-ChildItem -LiteralPath $packageDir -File -Recurse)
if ($packageFiles.Count -ne 2 -or
    @($packageFiles.Name | Where-Object { $_ -notin @('STS2Count.dll', 'STS2Count.json') }).Count -ne 0 -or
    $manifest.id -ne 'STS2Count' -or -not $manifest.has_dll -or $manifest.has_pck -or $manifest.affects_gameplay) {
    throw 'Unexpected package contents or manifest. Inspect dist/STS2Count before distribution.'
}
$archive = Join-Path $PSScriptRoot "dist\STS2Count-$($manifest.version).zip"
Compress-Archive -LiteralPath $packageDir -DestinationPath $archive -Force
Write-Output "Package: $archive"
Get-FileHash -LiteralPath $archive -Algorithm SHA256
