# Copyright © Erickson Lopez. MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "=== Verifying Copyright Headers on C# Source Files ===" -ForegroundColor Cyan

$expectedHeader = "// Copyright © Erickson Lopez. MIT License."
$violations = @()

$csFiles = Get-ChildItem -Path @("src", "tests", "samples", "benchmarks") -Recurse -Filter "*.cs" -File
foreach ($file in $csFiles) {
    if ($file.FullName -match "\\obj\\" -or $file.FullName -match "\\bin\\") {
        continue
    }

    $firstLine = (Get-Content -Path $file.FullName -TotalCount 1).Trim()
    if ($firstLine -ne $expectedHeader) {
        $violations += "Missing/invalid copyright header in: $($file.FullName) (Found: '$firstLine')"
    }
}

if ($violations.Count -gt 0) {
    Write-Host "`n[FAIL] Found $($violations.Count) copyright header violation(s):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[PASS] All $($csFiles.Count) C# source files contain the required copyright header." -ForegroundColor Green
exit 0
