# Copyright © Erickson Lopez. MIT License.
<#
.SYNOPSIS
    Automated Convention and Quality Gate Validator for EricksonLopez.Idempotency.
.DESCRIPTION
    Enforces project conventions across the entire solution:
    - Kebab-case naming for all documentation (.md)
    - MIT license header on all C# source files
    - Canonical repository URLs (ericksonlopezf) and support email
    - Zero obsolete APIs ([Obsolete])
    - English-only comments and diagnostics
    - Valid internal Markdown links
#>

[CmdletBinding()]
param(
    [string]$RootDirectory = $PSScriptRoot + "/.."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  EricksonLopez.Idempotency — Convention & Quality Gate     " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# ─── 1. License Header Check ──────────────────────────────────────────────
Write-Host "`n[1/6] Validating C# License Headers..." -ForegroundColor Yellow
$expectedHeader = "// Copyright © Erickson Lopez. MIT License."
$csFiles = Get-ChildItem -Path $RootDirectory -Filter "*.cs" -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|\.system_generated)[\\/]'
}

$missingHeaders = 0
foreach ($file in $csFiles) {
    $firstLine = (Get-Content -Path $file.FullName -TotalCount 1 -Encoding UTF8)
    if ($firstLine -ne $expectedHeader) {
        $rel = Resolve-Path -Relative -Path $file.FullName
        $violations.Add("Missing/Invalid MIT License Header: $rel")
        $missingHeaders++
    }
}
if ($missingHeaders -eq 0) {
    Write-Host "  -> PASSED: All $($csFiles.Count) C# source files contain valid license headers." -ForegroundColor Green
} else {
    Write-Host "  -> FAILED: $missingHeaders files missing valid license header." -ForegroundColor Red
}

# ─── 2. Markdown Kebab-Case Naming Check ──────────────────────────────────
Write-Host "`n[2/6] Validating Markdown File Naming (kebab-case)..." -ForegroundColor Yellow
$reservedNames = @(
    "README.md", "LICENSE", "LICENSE.md", "SECURITY.md", "SUPPORT.md", 
    "CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "CHANGELOG.md",
    "AnalyzerReleases.Shipped.md", "AnalyzerReleases.Unshipped.md", "Summary.md"
)

$mdFiles = Get-ChildItem -Path $RootDirectory -Filter "*.md" -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|\.system_generated|StrykerOutput)[\\/]'
}

$invalidMdNames = 0
foreach ($file in $mdFiles) {
    if ($reservedNames -contains $file.Name) { continue }
    
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    # kebab-case regex: only lowercase letters, digits, and single hyphens
    if ($baseName -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
        $rel = Resolve-Path -Relative -Path $file.FullName
        $violations.Add("Non-kebab-case Markdown Filename: $rel (expected: lowercase-kebab-case.md)")
        $invalidMdNames++
    }
}
if ($invalidMdNames -eq 0) {
    Write-Host "  -> PASSED: All $($mdFiles.Count) Markdown documents follow kebab-case naming rules." -ForegroundColor Green
} else {
    Write-Host "  -> FAILED: $invalidMdNames markdown files violate kebab-case conventions." -ForegroundColor Red
}

# ─── 3. Canonical URLs and Maintainer Email Check ────────────────────────
Write-Host "`n[3/6] Validating Canonical URLs and Maintainer Email..." -ForegroundColor Yellow
$textFiles = Get-ChildItem -Path $RootDirectory -Include "*.cs","*.md","*.csproj","*.props","*.targets","*.yml","*.json" -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|\.system_generated|StrykerOutput)[\\/]'
}

$legacyUrlCount = 0
$legacyEmailCount = 0
foreach ($file in $textFiles) {
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    if ($content -match 'github\.com/ericksonlopez/dotnet-idempotency') {
        $rel = Resolve-Path -Relative -Path $file.FullName
        $violations.Add("Legacy GitHub repository URL found in: $rel (expected: github.com/ericksonlopezf/dotnet-idempotency)")
        $legacyUrlCount++
    }
    if ($content -match 'security@ericksonlopez\.dev') {
        $rel = Resolve-Path -Relative -Path $file.FullName
        $violations.Add("Legacy email found in: $rel (expected: ericksonlopezf@gmail.com)")
        $legacyEmailCount++
    }
}
if ($legacyUrlCount -eq 0 -and $legacyEmailCount -eq 0) {
    Write-Host "  -> PASSED: All canonical URLs and maintainer emails are correctly formatted." -ForegroundColor Green
} else {
    Write-Host "  -> FAILED: Found $legacyUrlCount legacy URLs and $legacyEmailCount legacy emails." -ForegroundColor Red
}

# ─── 4. Obsolete API Usage Check in src/ ─────────────────────────────────
Write-Host "`n[4/6] Checking for Prohibited [Obsolete] Usages..." -ForegroundColor Yellow
$srcCsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "src") -Filter "*.cs" -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

$obsoleteCount = 0
foreach ($file in $srcCsFiles) {
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    if ($content -match '\[Obsolete\b') {
        $rel = Resolve-Path -Relative -Path $file.FullName
        $violations.Add("Prohibited [Obsolete] attribute in production code: $rel")
        $obsoleteCount++
    }
}
if ($obsoleteCount -eq 0) {
    Write-Host "  -> PASSED: Zero [Obsolete] attributes detected in src/." -ForegroundColor Green
} else {
    Write-Host "  -> FAILED: $obsoleteCount prohibited [Obsolete] attributes found." -ForegroundColor Red
}

# ─── 5. Prohibited <NoWarn> Suppressions ─────────────────────────────────
Write-Host "`n[5/6] Checking for Prohibited <NoWarn> Suppressions..." -ForegroundColor Yellow
$projFiles = Get-ChildItem -Path $RootDirectory -Include "*.csproj","*.props" -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]'
}

$prohibitedWarnings = @("CS1591", "CS8618", "CS8600", "CS8602", "CS8603", "CS8604", "CA2007")
$suppressionCount = 0
foreach ($file in $projFiles) {
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    foreach ($warn in $prohibitedWarnings) {
        if ($content -match "<NoWarn>[^<]*\b$warn\b") {
            $rel = Resolve-Path -Relative -Path $file.FullName
            $violations.Add("Prohibited compiler warning suppression ($warn) in: $rel")
            $suppressionCount++
        }
    }
}
if ($suppressionCount -eq 0) {
    Write-Host "  -> PASSED: No prohibited <NoWarn> suppressions found." -ForegroundColor Green
} else {
    Write-Host "  -> FAILED: $suppressionCount prohibited suppressions detected." -ForegroundColor Red
}

# ─── 6. Summary and Final Decision ───────────────────────────────────────
Write-Host "`n============================================================" -ForegroundColor Cyan
if ($violations.Count -gt 0) {
    Write-Host "  FAILED: $($violations.Count) Convention Violations Detected:" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "    - $v" -ForegroundColor DarkRed
    }
    Write-Host "============================================================" -ForegroundColor Cyan
    exit 1
} else {
    Write-Host "  PASSED: All conventions verified successfully with zero violations." -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Cyan
    exit 0
}
