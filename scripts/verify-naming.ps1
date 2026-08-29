# Copyright © Erickson Lopez. MIT License.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "=== Verifying File & Document Naming Conventions ===" -ForegroundColor Cyan

$violations = @()

# GitHub and open source standard root filenames
$allowedRootFiles = @(
    "README.md",
    "LICENSE",
    "SECURITY.md",
    "SUPPORT.md",
    "CONTRIBUTING.md",
    "CODE_OF_CONDUCT.md",
    "CHANGELOG.md"
)

# Standard .github special files
$allowedGithubFiles = @(
    "CODEOWNERS",
    "PULL_REQUEST_TEMPLATE.md",
    "dependabot.yml"
)

# 1. Verify docs kebab-case naming
if (Test-Path "docs") {
    $docFiles = Get-ChildItem -Path "docs" -Recurse -Filter "*.md" -File
    foreach ($doc in $docFiles) {
        $baseName = $doc.BaseName
        if ($baseName -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
            $violations += "Non-kebab-case documentation file in docs/: $($doc.FullName)"
        }
    }
}

# 2. Verify root Markdown files conform to standard allowed names or kebab-case
$rootMdFiles = Get-ChildItem -Path "." -Filter "*.md" -File
foreach ($file in $rootMdFiles) {
    if ($allowedRootFiles -notcontains $file.Name) {
        if ($file.BaseName -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
            $violations += "Non-standard/Non-kebab-case root documentation file: $($file.Name)"
        }
    }
}

# 3. Verify .github issue templates and workflows
if (Test-Path ".github/ISSUE_TEMPLATE") {
    $templateFiles = Get-ChildItem -Path ".github/ISSUE_TEMPLATE" -Recurse -Filter "*.md" -File
    foreach ($tmpl in $templateFiles) {
        if ($tmpl.BaseName -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
            $violations += "Non-kebab-case issue template in .github/ISSUE_TEMPLATE/: $($tmpl.FullName)"
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "`n[FAIL] Found $($violations.Count) naming violation(s):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[PASS] All documentation files follow kebab-case and standard naming conventions." -ForegroundColor Green
exit 0
