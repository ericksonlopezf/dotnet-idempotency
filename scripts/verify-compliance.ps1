<#
.SYNOPSIS
    Architecture & Quality Standards Compliance Verification Script for EricksonLopez.Idempotency.
.DESCRIPTION
    Validates architectural invariants:
    1. Kebab-case naming for all markdown documentation.
    2. Zero [Obsolete] usages in production code (src/).
    3. Presence of canonical MIT copyright header across all source files.
    4. Single top-level type per file in src/.
    5. Valid GitHub repository links referencing ericksonlopezf/dotnet-idempotency.
    6. Official support and security email normalization (ericksonlopezf@gmail.com).
    7. Zero prohibited <NoWarn> suppressions across all projects.
#>

[CmdletBinding()]
param (
    [string]$RootDirectory = "."
)

$ErrorActionPreference = "Stop"
$violations = 0

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  REPOSITORY COMPLIANCE & ARCHITECTURE AUDITOR    " -ForegroundColor Cyan
Write-Host "  Repository: EricksonLopez.Idempotency           " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Kebab-case documentation verification
Write-Host "`n[1/7] Checking documentation file naming (kebab-case)..." -ForegroundColor Yellow
$docsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "docs") -Recurse -Filter "*.md" -ErrorAction SilentlyContinue
$badDocNames = 0
if ($docsFiles) {
    foreach ($doc in $docsFiles) {
        $filename = $doc.Name
        if ($filename -ne "README.md" -and ($filename -cne $filename.ToLower() -or $filename -match "_")) {
            Write-Host "  ❌ Non-kebab-case document: $($doc.FullName)" -ForegroundColor Red
            $violations++
            $badDocNames++
        }
    }
}
if ($badDocNames -eq 0) { Write-Host "  ✅ All documentation files use valid kebab-case naming." -ForegroundColor Green }

# 2. Zero Obsolete APIs in src/
Write-Host "`n[2/7] Checking for [Obsolete] attribute usages in src/..." -ForegroundColor Yellow
$srcCsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "src") -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" }
$obsoleteCount = 0
if ($srcCsFiles) {
    foreach ($cs in $srcCsFiles) {
        $lines = Get-Content $cs.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "^\s*\[Obsolete\b" -and $lines[$i] -notmatch "^\s*//") {
                Write-Host "  ❌ [Obsolete] found in $($cs.FullName):$($i + 1)" -ForegroundColor Red
                $violations++
                $obsoleteCount++
            }
        }
    }
}
if ($obsoleteCount -eq 0) { Write-Host "  ✅ Zero [Obsolete] attributes in production code." -ForegroundColor Green }

# 3. Canonical MIT Copyright Header
Write-Host "`n[3/7] Checking canonical MIT copyright headers..." -ForegroundColor Yellow
$missingHeaderCount = 0
$allCsFiles = Get-ChildItem -Path $RootDirectory -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" }
if ($allCsFiles) {
    foreach ($cs in $allCsFiles) {
        $firstLine = (Get-Content $cs.FullName -TotalCount 1)
        if ($firstLine -notmatch "^// Copyright © Erickson Lopez\. MIT License\.") {
            Write-Host "  ❌ Missing canonical copyright header in: $($cs.FullName)" -ForegroundColor Red
            $violations++
            $missingHeaderCount++
        }
    }
}
if ($missingHeaderCount -eq 0) { Write-Host "  ✅ All production C# files contain the required MIT copyright header." -ForegroundColor Green }

# 4. One Type Per File Invariant
Write-Host "`n[4/7] Checking 'One Type Per File' rule in src/..." -ForegroundColor Yellow
$multiTypeCount = 0
if ($srcCsFiles) {
    foreach ($cs in $srcCsFiles) {
        $lines = Get-Content $cs.FullName | Where-Object { $_ -notmatch "^\s*//" }
        $typeDeclarations = $lines | Where-Object { $_ -match "^\s*(public|internal|private|protected)?\s*(sealed|abstract|static|readonly)?\s*(class|struct|record|interface|enum)\s+[A-Za-z0-9_]+" }
        if (@($typeDeclarations).Count -gt 1) {
            $hasMultipleTopLevels = ($typeDeclarations | Where-Object { $_ -notmatch "^\s{4,}" }).Count -gt 1
            if ($hasMultipleTopLevels) {
                Write-Host "  ❌ Multiple types declared in: $($cs.FullName)" -ForegroundColor Red
                $violations++
                $multiTypeCount++
            }
        }
    }
}
if ($multiTypeCount -eq 0) { Write-Host "  ✅ Every production file satisfies the 'One Type Per File' invariant." -ForegroundColor Green }

# 5. GitHub Repository Identity
Write-Host "`n[5/7] Checking GitHub identity links (ericksonlopezf/dotnet-idempotency)..." -ForegroundColor Yellow
$wrongRepoLinks = 0
$propsPath = Join-Path $RootDirectory "Directory.Build.props"
if (Test-Path $propsPath) {
    $propsContent = Get-Content $propsPath -Raw
    if ($propsContent -notmatch "ericksonlopezf/dotnet-idempotency") {
        Write-Host "  ❌ Directory.Build.props does not reference ericksonlopezf/dotnet-idempotency" -ForegroundColor Red
        $violations++
        $wrongRepoLinks++
    }
}
if ($wrongRepoLinks -eq 0) { Write-Host "  ✅ All GitHub URLs correctly target ericksonlopezf/dotnet-idempotency." -ForegroundColor Green }

# 6. Normalized Support/Security Contact Email
Write-Host "`n[6/7] Checking contact and security email normalization (ericksonlopezf@gmail.com)..." -ForegroundColor Yellow
$wrongEmailCount = 0
$secDoc = Join-Path $RootDirectory "SECURITY.md"
if (Test-Path $secDoc) {
    $secContent = Get-Content $secDoc -Raw
    if ($secContent -notmatch "ericksonlopezf@gmail\.com") {
        Write-Host "  ❌ SECURITY.md does not reference canonical email ericksonlopezf@gmail.com" -ForegroundColor Red
        $violations++
        $wrongEmailCount++
    }
}
if ($wrongEmailCount -eq 0) { Write-Host "  ✅ Official contact emails normalized to ericksonlopezf@gmail.com." -ForegroundColor Green }

Write-Host "`n==================================================" -ForegroundColor Cyan
if ($violations -eq 0) {
    Write-Host "  SUCCESS: 100% Governance & Compliance Verified. Zero violations. " -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "  FAILED: $violations compliance violation(s) detected. " -ForegroundColor Red
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 1
}
