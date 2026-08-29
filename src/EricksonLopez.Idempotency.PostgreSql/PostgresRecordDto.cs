// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency.PostgreSql;

/// <summary>
/// Internal DTO used for mapping rows directly from PostgreSQL query projections.
/// </summary>
internal sealed class PostgresRecordDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Scope { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
    public byte Status { get; init; }
    public Guid OwnerToken { get; init; }
    public int ConcurrencyVersion { get; init; }
    public int? ResponseStatusCode { get; init; }
    public string? ResponseHeaders { get; init; }
    public byte[]? ResponseBody { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LeaseExpiresAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public DateTimeOffset RetentionExpiresAtUtc { get; init; }
}
