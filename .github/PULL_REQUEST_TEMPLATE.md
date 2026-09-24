## Description

Please include a summary of the change and which issue is fixed (if any).
Include relevant motivation and context.

## Affected Packages

Please check all packages that are affected by this PR:
- [ ] `EricksonLopez.Idempotency.Abstractions`
- [ ] `EricksonLopez.Idempotency` (Core)
- [ ] `EricksonLopez.Idempotency.AspNetCore`
- [ ] `EricksonLopez.Idempotency.MariaDb`
- [ ] `EricksonLopez.Idempotency.Mediator`
- [ ] `EricksonLopez.Idempotency.MySql`
- [ ] `EricksonLopez.Idempotency.Oracle`
- [ ] `EricksonLopez.Idempotency.PostgreSql`
- [ ] `EricksonLopez.Idempotency.Redis`
- [ ] `EricksonLopez.Idempotency.Result`
- [ ] `EricksonLopez.Idempotency.Sqlite`
- [ ] `EricksonLopez.Idempotency.SqlServer`
- [ ] `EricksonLopez.Idempotency.Testing`

## Checklist

Before submitting this PR, please verify the following quality gates:
- [ ] I have performed a self-review of my own code.
- [ ] I have updated `CHANGELOG.md` under `[Unreleased]` (if applicable).
- [ ] Added or updated unit tests, integration tests, or architecture tests.
- [ ] Local build passes with 0 warnings and 0 errors (`dotnet build EricksonLopez.Idempotency.slnx -c Release`).
- [ ] All tests pass locally (`dotnet test EricksonLopez.Idempotency.slnx -c Release`).
- [ ] Architecture and repository compliance verified (`pwsh -File ./scripts/verify-compliance.ps1`).
- [ ] Link integrity and absolute URI validation passed (`pwsh -File ./scripts/verify-links.ps1`).
- [ ] File naming conventions verified (`pwsh -File ./scripts/verify-naming.ps1`).
- [ ] Stryker mutation testing maintains the **≥95%** break threshold (target: ≥98% low / ≥100% high).
- [ ] Benchmark regression gate confirmed: mean latency regression ≤ 5% vs baseline and 0 B heap allocation on hot paths.
