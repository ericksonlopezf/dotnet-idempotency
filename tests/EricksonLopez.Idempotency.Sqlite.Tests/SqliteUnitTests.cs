// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Idempotency.Sqlite.Tests;

public sealed class SqliteUnitTests
{
    [Fact]
    public void SqliteScripts_ContainsValidTableAndIndexesDDL()
    {
        var ddl = SqliteScripts.CreateTableScript;

        ddl.Should().NotBeNullOrWhiteSpace();
        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS idempotency_records");
        ddl.Should().Contain("PRIMARY KEY (tenant_id, scope, idempotency_key)");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_idempotency_records_retention");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_idempotency_records_stale_processing");
    }

    [Fact]
    public void Constructor_NullOrWhiteSpaceConnectionString_ThrowsArgumentException()
    {
        var act1 = () => new SqliteIdempotencyStore(null!);
        act1.Should().Throw<ArgumentException>();

        var act2 = () => new SqliteIdempotencyStore("   ");
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddSqliteIdempotencyStore_ValidationsAndRegistration()
    {
        var actNullServices = () => SqliteServiceCollectionExtensions.AddSqliteIdempotencyStore(null!, "Data Source=test.db;");
        actNullServices.Should().Throw<ArgumentNullException>().WithParameterName("services");

        var services = new ServiceCollection();
        var actNullCs = () => services.AddSqliteIdempotencyStore(null!);
        actNullCs.Should().Throw<ArgumentException>();

        var result = services.AddSqliteIdempotencyStore("Data Source=test.db;");
        result.Should().BeSameAs(services);

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IIdempotencyStore>();

        store.Should().NotBeNull();
        store.Should().BeOfType<SqliteIdempotencyStore>();
    }

    [Fact]
    public async Task TryAcquireAsync_NewKey_ReturnsAcquiredNewAndFormatsDatesInIso8601()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-new");
        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));

        claim.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        claim.IsAcquired.Should().BeTrue();
        claim.OwnerToken.Should().NotBeNull();
        claim.OwnerToken.Should().NotBe(Guid.Empty);
        claim.ConcurrencyVersion.Should().Be(1);

        // Verify exact ISO 8601 "O" roundtrip date format in database
        using var cmd = masterConnection.CreateCommand();
        cmd.CommandText = "SELECT created_at_utc, lease_expires_at_utc, retention_expires_at_utc FROM idempotency_records WHERE idempotency_key = 'sqlite-key-new'";
        using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        var createdAt = reader.GetString(0);
        var leaseExpiresAt = reader.GetString(1);
        var retentionExpiresAt = reader.GetString(2);

        var parsedCreatedAt = DateTimeOffset.ParseExact(createdAt, "O", CultureInfo.InvariantCulture);
        var parsedLease = DateTimeOffset.ParseExact(leaseExpiresAt, "O", CultureInfo.InvariantCulture);
        var parsedRetention = DateTimeOffset.ParseExact(retentionExpiresAt, "O", CultureInfo.InvariantCulture);

        parsedLease.Should().BeAfter(parsedCreatedAt);
        parsedRetention.Should().BeAfter(parsedLease);
    }

    [Fact]
    public async Task TryAcquireAsync_CompletedReplay_ReplaysCachedResponseAndHeaders()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-completed");
        var fingerprint = "fp-completed";

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim.Status.Should().Be(ClaimResultStatus.AcquiredNew);

        var headers = new Dictionary<string, string[]>
        {
            ["X-Test"] = new[] { "Val1", "Val2" },
            ["X-Empty"] = Array.Empty<string>()
        };
        var bodyBytes = new byte[] { 10, 20, 30 };

        var completed = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            claim.OwnerToken!.Value,
            claim.ConcurrencyVersion!.Value,
            statusCode: 201,
            headers: headers,
            responseBody: bodyBytes,
            retentionDuration: TimeSpan.FromDays(7));

        completed.Should().BeTrue();

        // Verify completed_at_utc and retention_expires_at_utc date format in DB
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = "SELECT completed_at_utc, retention_expires_at_utc FROM idempotency_records WHERE idempotency_key = 'sqlite-key-completed'";
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            var completedAt = reader.GetString(0);
            var retentionExpiresAt = reader.GetString(1);
            DateTimeOffset.ParseExact(completedAt, "O", CultureInfo.InvariantCulture).Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(1));
            DateTimeOffset.ParseExact(retentionExpiresAt, "O", CultureInfo.InvariantCulture).Should().BeAfter(DateTimeOffset.UtcNow);
        }

        var replayClaim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        replayClaim.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        replayClaim.IsReplay.Should().BeTrue();
        replayClaim.CachedResponse.Should().NotBeNull();
        replayClaim.CachedResponse!.StatusCode.Should().Be(201);
        replayClaim.CachedResponse.Headers.Should().ContainKey("X-Test");
        replayClaim.CachedResponse.Body.ToArray().Should().BeEquivalentTo(bodyBytes);
        replayClaim.ExistingFingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public async Task TryAcquireAsync_CompletedReplay_WithNullHeadersAndNullBodyAndNullStatusCode_UsesDefaults()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-null-defaults");
        var fingerprint = "fp-defaults";
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // Manually insert completed record with NULL headers, body, status code
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO idempotency_records (
                    id, tenant_id, scope, idempotency_key, fingerprint, status,
                    owner_token, concurrency_version, response_status_code, response_headers, response_body,
                    created_at_utc, lease_expires_at_utc, completed_at_utc, retention_expires_at_utc
                )
                VALUES (
                    'rec-1', @TenantId, 'orders', @Key, @Fingerprint, 2,
                    'owner-1', 1, NULL, NULL, NULL,
                    @Now, @Now, @Now, @Now
                );
                """;
            cmd.Parameters.AddWithValue("@TenantId", tenantId.ToString());
            cmd.Parameters.AddWithValue("@Key", key.Value);
            cmd.Parameters.AddWithValue("@Fingerprint", fingerprint);
            cmd.Parameters.AddWithValue("@Now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        var replayClaim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        replayClaim.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        replayClaim.CachedResponse.Should().NotBeNull();
        replayClaim.CachedResponse!.StatusCode.Should().Be(200);
        replayClaim.CachedResponse.Headers.Should().BeEmpty();
        replayClaim.CachedResponse.Body.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task TryAcquireAsync_FingerprintMismatch_ReturnsMismatchStatusAndExistingFingerprint()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-mismatch");

        var claim1 = await store.TryAcquireAsync(tenantId, "orders", key, "fp-original", TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim1.Status.Should().Be(ClaimResultStatus.AcquiredNew);

        var claim2 = await store.TryAcquireAsync(tenantId, "orders", key, "fp-different", TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim2.Status.Should().Be(ClaimResultStatus.FingerprintMismatch);
        claim2.ExistingFingerprint.Should().Be("fp-original");
    }

    [Fact]
    public async Task TryAcquireAsync_InFlightConflict_WhenProcessingAndLeaseActive()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-inflight");
        var fingerprint = "fp-inflight";

        var claim1 = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(10), TimeSpan.FromDays(7));
        claim1.Status.Should().Be(ClaimResultStatus.AcquiredNew);

        // Add trigger to ensure StealLeaseSql UPDATE is NOT executed when lease is active (verifying early return)
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = "CREATE TRIGGER trg_no_update_active_lease BEFORE UPDATE ON idempotency_records BEGIN SELECT RAISE(ABORT, 'UPDATE should not execute for active lease'); END;";
            await cmd.ExecuteNonQueryAsync();
        }

        var claim2 = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(10), TimeSpan.FromDays(7));
        claim2.Status.Should().Be(ClaimResultStatus.InFlightConflict);
        claim2.ExistingFingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public async Task TryAcquireAsync_InFlightConflict_WhenLeaseBoundaryIsExactUtcNow()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-exact-lease");
        var fingerprint = "fp-exact";
        var exactFutureTime = DateTimeOffset.UtcNow.AddSeconds(10).ToString("O", CultureInfo.InvariantCulture);

        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO idempotency_records (
                    id, tenant_id, scope, idempotency_key, fingerprint, status,
                    owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
                )
                VALUES (
                    'rec-exact', @TenantId, 'orders', @Key, @Fingerprint, 1,
                    'owner-exact', 1, @ExactTime, @ExactTime, @ExactTime
                );
                """;
            cmd.Parameters.AddWithValue("@TenantId", tenantId.ToString());
            cmd.Parameters.AddWithValue("@Key", key.Value);
            cmd.Parameters.AddWithValue("@Fingerprint", fingerprint);
            cmd.Parameters.AddWithValue("@ExactTime", exactFutureTime);
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE TRIGGER trg_no_update_boundary BEFORE UPDATE ON idempotency_records BEGIN SELECT RAISE(ABORT, 'UPDATE should not execute for active lease boundary'); END;";
            await cmd.ExecuteNonQueryAsync();
        }

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenExistingDeletedBetweenConflictAndSelect_ReturnsInFlightConflict()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        // Simulate error 19 during INSERT where row does not actually exist on SELECT
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = "CREATE TRIGGER trg_simulate_conflict BEFORE INSERT ON idempotency_records BEGIN SELECT RAISE(ABORT, 'PRIMARY KEY must be unique'); END;";
            await cmd.ExecuteNonQueryAsync();
        }

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-missing-row");
        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task TryAcquireAsync_StealLease_WhenProcessingAndLeaseExpired_ReturnsAcquiredStale()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-stale-lease");
        var fingerprint = "fp-stale";
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture);
        var retention = DateTimeOffset.UtcNow.AddDays(7).ToString("O", CultureInfo.InvariantCulture);

        // Insert record with lease in the past
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO idempotency_records (
                    id, tenant_id, scope, idempotency_key, fingerprint, status,
                    owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
                )
                VALUES (
                    'rec-stale', @TenantId, 'orders', @Key, @Fingerprint, 1,
                    'old-owner', 1, @PastTime, @PastTime, @Retention
                );
                """;
            cmd.Parameters.AddWithValue("@TenantId", tenantId.ToString());
            cmd.Parameters.AddWithValue("@Key", key.Value);
            cmd.Parameters.AddWithValue("@Fingerprint", fingerprint);
            cmd.Parameters.AddWithValue("@PastTime", pastTime);
            cmd.Parameters.AddWithValue("@Retention", retention);
            await cmd.ExecuteNonQueryAsync();
        }

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim.Status.Should().Be(ClaimResultStatus.AcquiredStale);
        claim.IsAcquired.Should().BeTrue();
        claim.ConcurrencyVersion.Should().Be(2);
        claim.OwnerToken.Should().NotBeNull();

        // Verify updated dates in DB
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = "SELECT lease_expires_at_utc FROM idempotency_records WHERE idempotency_key = 'sqlite-key-stale-lease'";
            var newLease = (string)(await cmd.ExecuteScalarAsync())!;
            DateTimeOffset.ParseExact(newLease, "O", CultureInfo.InvariantCulture).Should().BeAfter(DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task TryAcquireAsync_StealLease_WhenStatusIsFailed_ReturnsAcquiredStale()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-failed-retry");
        var fingerprint = "fp-failed";

        var claim1 = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim1.Status.Should().Be(ClaimResultStatus.AcquiredNew);

        var failed = await store.MarkFailedAsync(tenantId, "orders", key, claim1.OwnerToken!.Value, claim1.ConcurrencyVersion!.Value);
        failed.Should().BeTrue();

        // Verify completed_at_utc date format on failed record
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = "SELECT completed_at_utc FROM idempotency_records WHERE idempotency_key = 'sqlite-key-failed-retry'";
            var completedAt = (string)(await cmd.ExecuteScalarAsync())!;
            DateTimeOffset.ParseExact(completedAt, "O", CultureInfo.InvariantCulture).Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(1));
        }

        var retryClaim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        retryClaim.Status.Should().Be(ClaimResultStatus.AcquiredStale);
        retryClaim.IsAcquired.Should().BeTrue();
        retryClaim.ConcurrencyVersion.Should().Be(2);
    }

    [Fact]
    public async Task MarkCompletedAsync_WrongOwnerTokenOrVersion_ReturnsFalse()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-wrong-owner");
        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));

        var resultWrongOwner = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            ownerToken: Guid.NewGuid(), // Invalid owner token
            concurrencyVersion: 1,
            statusCode: 200,
            headers: new Dictionary<string, string[]>(),
            responseBody: ReadOnlyMemory<byte>.Empty,
            retentionDuration: TimeSpan.FromDays(1));

        resultWrongOwner.Should().BeFalse();

        var resultWrongVersion = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            ownerToken: claim.OwnerToken!.Value,
            concurrencyVersion: 99, // Invalid version
            statusCode: 200,
            headers: new Dictionary<string, string[]>(),
            responseBody: ReadOnlyMemory<byte>.Empty,
            retentionDuration: TimeSpan.FromDays(1));

        resultWrongVersion.Should().BeFalse();
    }

    [Fact]
    public async Task MarkFailedAsync_WrongOwnerTokenOrVersion_ReturnsFalse()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-fail-wrong");
        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));

        var resultWrongOwner = await store.MarkFailedAsync(
            tenantId,
            "orders",
            key,
            ownerToken: Guid.NewGuid(),
            concurrencyVersion: 1);

        resultWrongOwner.Should().BeFalse();

        var resultWrongVersion = await store.MarkFailedAsync(
            tenantId,
            "orders",
            key,
            ownerToken: claim.OwnerToken!.Value,
            concurrencyVersion: 88);

        resultWrongVersion.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredRecordsAsync_DeletesOnlyExpiredRecordsWithinBatchSize()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();

        // Insert 3 expired records and 2 active records
        for (var i = 1; i <= 3; i++)
        {
            var key = new IdempotencyKey($"expired-key-{i}");
            var claim = await store.TryAcquireAsync(tenantId, "scope", key, $"fp-{i}", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            await store.MarkCompletedAsync(
                tenantId,
                "scope",
                key,
                claim.OwnerToken!.Value,
                claim.ConcurrencyVersion!.Value,
                200,
                new Dictionary<string, string[]>(),
                ReadOnlyMemory<byte>.Empty,
                retentionDuration: TimeSpan.FromMilliseconds(-100)); // Already expired
        }

        for (var i = 1; i <= 2; i++)
        {
            var key = new IdempotencyKey($"active-key-{i}");
            var claim = await store.TryAcquireAsync(tenantId, "scope", key, $"fp-act-{i}", TimeSpan.FromMinutes(10), TimeSpan.FromDays(10));
            await store.MarkCompletedAsync(
                tenantId,
                "scope",
                key,
                claim.OwnerToken!.Value,
                claim.ConcurrencyVersion!.Value,
                200,
                new Dictionary<string, string[]>(),
                ReadOnlyMemory<byte>.Empty,
                retentionDuration: TimeSpan.FromDays(10)); // Far in the future
        }

        var deleted = await store.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow, batchSize: 10);
        deleted.Should().Be(3);

        // Active records must still exist and be replayable
        var activeClaim = await store.TryAcquireAsync(tenantId, "scope", new IdempotencyKey("active-key-1"), "fp-act-1", TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
        activeClaim.Status.Should().Be(ClaimResultStatus.CompletedReplay);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenStealFailsDueToUnstealableStatus_ReturnsInFlightConflict()
    {
        var (connectionString, masterConnection) = await CreateTestDatabaseAsync();
        using var _ = masterConnection;
        var store = new SqliteIdempotencyStore(connectionString);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("sqlite-key-unknown-status");
        var fingerprint = "fp-unknown";
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // Insert record with status = 4 (unstealable)
        using (var cmd = masterConnection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO idempotency_records (
                    id, tenant_id, scope, idempotency_key, fingerprint, status,
                    owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
                )
                VALUES (
                    'rec-unknown', @TenantId, 'orders', @Key, @Fingerprint, 4,
                    'owner-1', 1, @Now, @Now, @Now
                );
                """;
            cmd.Parameters.AddWithValue("@TenantId", tenantId.ToString());
            cmd.Parameters.AddWithValue("@Key", key.Value);
            cmd.Parameters.AddWithValue("@Fingerprint", fingerprint);
            cmd.Parameters.AddWithValue("@Now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        claim.Status.Should().Be(ClaimResultStatus.InFlightConflict);
        claim.ExistingFingerprint.Should().Be(fingerprint);
    }

    private static async Task<(string ConnectionString, SqliteConnection MasterConnection)> CreateTestDatabaseAsync()
    {
        var connectionString = $"Data Source=file:memdb_{Guid.NewGuid():N}?mode=memory&cache=shared";
        var masterConnection = new SqliteConnection(connectionString);
        await masterConnection.OpenAsync();

        using var cmd = masterConnection.CreateCommand();
        cmd.CommandText = SqliteScripts.CreateTableScript;
        await cmd.ExecuteNonQueryAsync();

        return (connectionString, masterConnection);
    }
}
