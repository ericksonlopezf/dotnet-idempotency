# Copyright © Erickson Lopez. MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "=== Verifying Markdown Links & Absolute Path Invariants ===" -ForegroundColor Cyan

$violations = @()

$mdFiles = Get-ChildItem -Path @("docs", "README.md", "CONTRIBUTING.md", "SECURITY.md", "SUPPORT.md", "CHANGELOG.md", "CODE_OF_CONDUCT.md") -Recurse -Filter "*.md" -File
foreach ($file in $mdFiles) {
    $content = Get-Content -Path $file.FullName -Raw

    # 1. Disallow file:/// absolute URIs
    if ($content -match "file:///") {
        $violations += "Forbidden absolute 'file:///' URI found in: $($file.FullName)"
    }

    # 2. Check local markdown link targets exist (exclude http, https, mailto, and fragment links)
    $matches = [regex]::Matches($content, '\[([^\]]+)\]\((?!(?:https?:\/\/|mailto:|#))([^\)]+)\)')
    foreach ($m in $matches) {
        $target = $m.Groups[2].Value.Split('#')[0]
        if (-not [string]::IsNullOrWhiteSpace($target)) {
            $dir = Split-Path -Path $file.FullName -Parent
            $targetPath = Join-Path -Path $dir -ChildPath $target
            if (-not (Test-Path -Path $targetPath)) {
                $violations += "Broken relative link target '$target' in: $($file.FullName)"
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "`n[FAIL] Found $($violations.Count) link violation(s):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[PASS] All documentation links are valid and free from absolute path leaks." -ForegroundColor Green
exit 0
