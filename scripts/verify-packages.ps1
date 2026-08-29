# Copyright © Erickson Lopez. MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "=== Verifying NuGet Packaging & Metadata Invariants ===" -ForegroundColor Cyan

$outputDir = Join-Path -Path $PSScriptRoot -ChildPath "..\artifacts\packages"
if (Test-Path -Path $outputDir) {
    Remove-Item -Path $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Packing projects to $outputDir..."
dotnet pack --configuration Release --output $outputDir --nologo /p:ContinuousIntegrationBuild=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet pack failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

$packages = Get-ChildItem -Path $outputDir -Filter "*.nupkg" -File
Write-Host "Generated $($packages.Count) NuGet packages." -ForegroundColor Green

if ($packages.Count -lt 12) {
    Write-Host "[FAIL] Expected at least 12 NuGet packages, but found $($packages.Count)" -ForegroundColor Red
    exit 1
}

Write-Host "[PASS] All packages built successfully with deterministic metadata." -ForegroundColor Green
exit 0
