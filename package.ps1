<#
.SYNOPSIS
    Packages PaintDotNet.QuickExport for release.
.DESCRIPTION
    Builds the standalone single-file patcher executable, bundles it with 
    documentation, creates a versioned distribution ZIP archive in dist/,
    and computes the SHA-256 checksum.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$distDir = Join-Path $repoRoot "dist"
$stagingDir = Join-Path $distDir "staging"

# 1. Read version from Directory.Build.props
if (-not (Test-Path $propsPath)) {
    throw "Directory.Build.props not found at: $propsPath"
}

[xml]$propsXml = Get-Content $propsPath
$version = $propsXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = "1.0.0"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Packaging PaintDotNet-QuickExport v$version (Release)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. Prepare directories
if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$zipName = "QuickExportPatcher-v$version-win-x64.zip"
$zipPath = Join-Path $distDir $zipName

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

# 3. Publish single-file patcher
Write-Host "`n[1/4] Publishing single-file patcher executable..." -ForegroundColor Yellow
$projectPath = Join-Path $repoRoot "src/PaintDotNet.QuickExport.Patcher/PaintDotNet.QuickExport.Patcher.csproj"

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "false",
    "-p:PublishSingleFile=true",
    "-o", $stagingDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 4. Remove debug symbols from staging folder
Get-ChildItem -Path $stagingDir -Filter "*.pdb" | Remove-Item -Force

# 5. Include documentation in the package
Write-Host "`n[2/4] Bundling documentation..." -ForegroundColor Yellow
Copy-Item (Join-Path $repoRoot "LICENSE") -Destination $stagingDir
Copy-Item (Join-Path $repoRoot "README.md") -Destination $stagingDir

# 6. Create ZIP archive
Write-Host "`n[3/4] Creating distribution archive ($zipName)..." -ForegroundColor Yellow
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -Force

# Clean up staging folder
Remove-Item -Recurse -Force $stagingDir

# 7. Compute SHA-256 Checksum
Write-Host "`n[4/4] Computing SHA-256 checksum..." -ForegroundColor Yellow
$hashResult = Get-FileHash -Path $zipPath -Algorithm SHA256
$checksumLine = "$($hashResult.Hash.ToLower())  $zipName"
$checksumFile = Join-Path $distDir "checksums.txt"
Set-Content -Path $checksumFile -Value $checksumLine -Encoding UTF8

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Package created successfully!" -ForegroundColor Green
Write-Host " Output:   $zipPath" -ForegroundColor White
Write-Host " Checksum: $checksumLine" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
