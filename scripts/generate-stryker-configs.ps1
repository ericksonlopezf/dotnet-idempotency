# Copyright © Erickson Lopez. MIT License.
<#
.SYNOPSIS
    Generates and synchronizes all Stryker.NET mutation testing configuration profiles.
.DESCRIPTION
    Ensures that all 13 package modules have a fully compliant, synchronized
    stryker-*-config.json file adhering to the 100/98/95 threshold policy,
    concurrency 2, and required reporter settings.
.PARAMETER VerifyOnly
    When specified, verifies that all existing files match generated output without writing.
#>
[CmdletBinding()]
param(
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

$packages = @(
    @{ Slug = "abstractions"; Project = "EricksonLopez.Idempotency.Abstractions.csproj"; TestProject = "EricksonLopez.Idempotency.Abstractions.Tests.csproj" },
    @{ Slug = "core";         Project = "EricksonLopez.Idempotency.csproj";              TestProject = "EricksonLopez.Idempotency.Tests.csproj" },
    @{ Slug = "aspnetcore";   Project = "EricksonLopez.Idempotency.AspNetCore.csproj";    TestProject = "EricksonLopez.Idempotency.AspNetCore.Tests.csproj" },
    @{ Slug = "mediator";     Project = "EricksonLopez.Idempotency.Mediator.csproj";      TestProject = "EricksonLopez.Idempotency.Mediator.Tests.csproj" },
    @{ Slug = "result";       Project = "EricksonLopez.Idempotency.Result.csproj";        TestProject = "EricksonLopez.Idempotency.Result.Tests.csproj" },
    @{ Slug = "testing";      Project = "EricksonLopez.Idempotency.Testing.csproj";       TestProject = "EricksonLopez.Idempotency.Testing.Tests.csproj" },
    @{ Slug = "postgresql";   Project = "EricksonLopez.Idempotency.PostgreSql.csproj";    TestProject = "EricksonLopez.Idempotency.PostgreSql.Tests.csproj" },
    @{ Slug = "sqlserver";    Project = "EricksonLopez.Idempotency.SqlServer.csproj";     TestProject = "EricksonLopez.Idempotency.SqlServer.Tests.csproj" },
    @{ Slug = "mysql";        Project = "EricksonLopez.Idempotency.MySql.csproj";         TestProject = "EricksonLopez.Idempotency.MySql.Tests.csproj" },
    @{ Slug = "mariadb";      Project = "EricksonLopez.Idempotency.MariaDb.csproj";       TestProject = "EricksonLopez.Idempotency.MariaDb.Tests.csproj" },
    @{ Slug = "oracle";       Project = "EricksonLopez.Idempotency.Oracle.csproj";        TestProject = "EricksonLopez.Idempotency.Oracle.Tests.csproj" },
    @{ Slug = "sqlite";       Project = "EricksonLopez.Idempotency.Sqlite.csproj";        TestProject = "EricksonLopez.Idempotency.Sqlite.Tests.csproj" },
    @{ Slug = "redis";        Project = "EricksonLopez.Idempotency.Redis.csproj";         TestProject = "EricksonLopez.Idempotency.Redis.Tests.csproj" }
)

Write-Host "=== Stryker Configuration Profile Synchronizer ===" -ForegroundColor Cyan
Write-Host "Verifying $($packages.Count) Stryker configuration profiles..." -ForegroundColor Gray

$mismatches = @()

foreach ($pkg in $packages) {
    $fileName = "stryker-$($pkg.Slug)-config.json"
    $targetPath = Join-Path $repoRoot $fileName

    $configObj = [ordered]@{
        "stryker-config" = [ordered]@{
            "ignore-methods"    = @("ConfigureAwait", "Dispose")
            "test-projects"     = @($pkg.TestProject)
            "project"           = $pkg.Project
            "reporters"         = @("html", "json", "cleartext", "progress")
            "mutate"            = @(
                "**/*.cs",
                "!bin/**",
                "!obj/**",
                "!**/*.g.cs",
                "!**/*.AssemblyInfo.cs"
            )
            "thresholds"        = [ordered]@{
                "break" = 95
                "high"  = 100
                "low"   = 98
            }
            "coverage-analysis" = "all"
            "concurrency"       = 2
        }
    }

    $jsonContent = ($configObj | ConvertTo-Json -Depth 5) + "`n"

    if ($VerifyOnly) {
        if (-not (Test-Path $targetPath)) {
            $mismatches += "Missing config file: $fileName"
        } else {
            $existing = Get-Content -Path $targetPath -Raw
            # Compare normalized whitespace
            $normExisting = ($existing | ConvertFrom-Json | ConvertTo-Json -Depth 5)
            $normExpected = ($jsonContent | ConvertFrom-Json | ConvertTo-Json -Depth 5)
            if ($normExisting -ne $normExpected) {
                $mismatches += "Configuration drift detected in: $fileName"
            }
        }
    } else {
        [System.IO.File]::WriteAllText($targetPath, $jsonContent, [System.Text.Encoding]::UTF8)
        Write-Host "  ✅ Generated $fileName" -ForegroundColor Green
    }
}

if ($VerifyOnly) {
    if ($mismatches.Count -gt 0) {
        Write-Host "`n❌ Stryker configuration verification failed:" -ForegroundColor Red
        $mismatches | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "`n✅ All 13 Stryker configuration profiles are synchronized and valid." -ForegroundColor Green
} else {
    Write-Host "`nSuccessfully synchronized all $($packages.Count) Stryker configuration profiles." -ForegroundColor Green
}
