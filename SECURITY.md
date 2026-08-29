# Security Policy

---

## 1. Supported Versions

We provide security patches and updates for the following versions of `EricksonLopez.Idempotency`:

| Version | Target Framework | Status | Supported Until |
|---|---|---|---|
| **1.0.x** | `.NET 10.0` (`net10.0`) | **Current / Active** | November 2028 (Aligned with .NET 10 LTS) |
| **1.0.x** | `.NET 9.0` (`net9.0`) | **Active** | May 2026 (Aligned with .NET 9 STS) |
| **1.0.x** | `.NET 8.0` (`net8.0`) | **Active** | November 2026 (Aligned with .NET 8 LTS) |
| `< 1.0.0` | Any | End of Life | Not Supported |

---

## 2. Reporting a Vulnerability

If you discover a security vulnerability in `EricksonLopez.Idempotency`, please report it responsibly:

1. **Do not create a public GitHub Issue.**
2. Send an email directly to **Erickson López** at **ericksonlopezf@gmail.com** with:
   - Vulnerability description and impact assessment
   - Proof of Concept (PoC) or reproducible test case
   - Affected package(s) and version(s)
3. Alternatively, submit a **Private Vulnerability Report** via [GitHub Security Advisories](https://github.com/ericksonlopezf/dotnet-idempotency/security/advisories/new).

### Response Timeline

- **Acknowledgment**: Within 48 hours of initial report.
- **Triage & Assessment**: Within 5 business days.
- **Remediation & Patch Release**: Targeted within 14 calendar days depending on severity.
- **Public Disclosure**: Coordinated after the patched release is published on NuGet.org.

---

## 3. Architectural Security Invariants & Boundaries

`EricksonLopez.Idempotency` is designed around strict defensive security primitives:

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                       SECURITY INVARIANTS MATRIX                            │
├──────────────────────────────┬──────────────────────────────────────────────┤
│ Security Mechanism           │ Protection Guarantee                         │
├──────────────────────────────┼──────────────────────────────────────────────┤
│ Cryptographic Fingerprinting │ SHA-256 canonical hashing prevents malicious │
│                              │ key reuse with altered payloads (tampering). │
│ Multi-Tenant Partitioning    │ State is strictly keyed by (TenantId, Scope, │
│                              │ IdempotencyKey), preventing crosstalk.       │
│ Distributed Fencing Tokens   │ Monotonically increasing concurrency tokens  │
│                              │ prevent split-brain commits by zombie nodes. │
│ Max Body Size Limits         │ Configurable MaxRequestBodySizeBytes buffer  │
│                              │ caps prevent Denial of Service (DoS) memory. │
└──────────────────────────────┴──────────────────────────────────────────────┘
```

---

## 4. Supply Chain Security

- **Central Package Management (CPM)**: All transitive and direct dependencies are pinned centrally in `Directory.Packages.props`.
- **Strong Name Signing**: All assembly binaries are strongly named using `EricksonLopez.snk` and verified at compile time. The private key is stored as the `SNK_KEY` GitHub Actions secret (base64-encoded).
- **Reproducible Builds & SourceLink**: Symbol packages (`.snupkg`) and SourceLink metadata are published with every package.
- **Automated Dependency Auditing**: Dependabot scans NuGet dependencies and GitHub Actions workflows weekly for known Common Vulnerabilities and Exposures (CVEs).

---

*Copyright © Erickson Lopez. MIT License.*
