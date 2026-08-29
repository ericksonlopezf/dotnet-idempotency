## Description

Please include a summary of the change and which issue is fixed (if any).
Include relevant motivation and context.

## Affected Packages
Please check all packages that are affected by this PR:
- [ ] `EricksonLopez.Idempotency` (Core)
- [ ] `EricksonLopez.Idempotency.Abstractions`
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

Before submitting this PR, please verify the following:
- [ ] I have performed a self-review of my own code.
- [ ] I have updated the `CHANGELOG.md` (if applicable).
- [ ] I have added/updated unit tests or integration tests.
- [ ] Local build passes (`dotnet build EricksonLopez.Idempotency.slnx -c Release`).
- [ ] Local tests pass (`dotnet test EricksonLopez.Idempotency.slnx`).
- [ ] I verified compliance using `./scripts/verify-compliance.ps1`.
- [ ] Stryker mutation testing maintains the **95%** mutation score threshold.
- [ ] Benchmarks confirmed no regressions.
