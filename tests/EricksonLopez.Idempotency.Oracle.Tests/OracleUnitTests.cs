// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Oracle;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Idempotency.Oracle.Tests;

public sealed class OracleUnitTests
{
    private const string DummyConnectionString = "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=127.0.0.1)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=XE)));User Id=test;Password=secret;Connection Timeout=1;";

    [Fact]
    public void OracleScripts_ContainsValidTableAndIndexesDDL()
    {
        var ddl = OracleScripts.CreateTableScript;

        ddl.Should().NotBeNullOrWhiteSpace();
        ddl.Should().Contain("CREATE TABLE idempotency_records");
        ddl.Should().Contain("CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, scope, idempotency_key)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidConnectionString_ThrowsArgumentException(string? connStr)
    {
        var act = () => new OracleIdempotencyStore(connStr!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddOracleIdempotencyStore_ValidationsAndRegistration()
    {
        var actNullServices = () => OracleServiceCollectionExtensions.AddOracleIdempotencyStore(null!, DummyConnectionString);
        actNullServices.Should().Throw<ArgumentNullException>().WithParameterName("services");

        var actInvalidConn = () => new ServiceCollection().AddOracleIdempotencyStore(" ");
        actInvalidConn.Should().Throw<ArgumentException>();

        var services = new ServiceCollection();
        var result = services.AddOracleIdempotencyStore(DummyConnectionString);
        result.Should().BeSameAs(services);

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IIdempotencyStore>();
        var txStore = provider.GetService<ITransactionalIdempotencyStore>();

        store.Should().NotBeNull();
        store.Should().BeOfType<OracleIdempotencyStore>();
        txStore.Should().NotBeNull();
        txStore.Should().BeSameAs(store);
    }

    [Fact]
    public async Task PublicFacadeMethods_DelegateToConnection()
    {
        var store = new OracleIdempotencyStore(DummyConnectionString);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-facade-key");

        var actAcquire = () => store.TryAcquireAsync(tenantId, "scope", key, "fp", TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
        await actAcquire.Should().ThrowAsync<Exception>();

        var actCompleted = () => store.MarkCompletedAsync(tenantId, "scope", key, Guid.NewGuid(), 1, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromDays(1));
        await actCompleted.Should().ThrowAsync<Exception>();

        var actFailed = () => store.MarkFailedAsync(tenantId, "scope", key, Guid.NewGuid(), 1);
        await actFailed.Should().ThrowAsync<Exception>();

        var actCleanup = () => store.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow, 10);
        await actCleanup.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenInsertSucceeds_ReturnsAcquiredNew()
    {
        using var connection = new TestDbConnection(onExecuteNonQuery: _ => 1);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-1");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            transaction: null,
            tenantId,
            "orders",
            key,
            "fp-123",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        result.IsAcquired.Should().BeTrue();
        result.OwnerToken.Should().NotBeNull();
        result.OwnerToken.Should().NotBe(Guid.Empty);
        result.ConcurrencyVersion.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenInsertCollidesAndExistingDeleted_ReturnsInFlightConflict()
    {
        using var connection = new TestDbConnection(
            onExecuteNonQuery: _ => 0,
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>>()));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-missing");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-123",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenInsertThrowsDbException_FallsThroughToSelect()
    {
        var existingRow = CreateRow(
            status: 1,
            fingerprint: "fp-collision",
            leaseExpires: DateTimeOffset.UtcNow.AddMinutes(5));

        using var connection = new TestDbConnection(
            onExecuteNonQuery: _ => throw new TestDbException("ORA-00001: unique constraint violated"),
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-collision");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-collision",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenFingerprintMismatches_ReturnsFingerprintMismatch()
    {
        var existingRow = CreateRow(
            status: 1,
            fingerprint: "fp-original",
            leaseExpires: DateTimeOffset.UtcNow.AddMinutes(5));

        using var connection = new TestDbConnection(
            onExecuteNonQuery: _ => 0,
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-mismatch");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-different",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.FingerprintMismatch);
        result.ExistingFingerprint.Should().Be("fp-original");
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenCompleted_ReturnsCompletedReplayWithCachedResponse()
    {
        var headers = new Dictionary<string, string[]> { ["X-Header"] = new[] { "V1" } };
        var headersJson = JsonSerializer.Serialize(headers, IdempotencyJsonContext.Default.DictionaryStringStringArray);
        var bodyBytes = new byte[] { 1, 2, 3 };

        var existingRow = CreateRow(
            status: 2,
            fingerprint: "fp-completed",
            statusCode: 201,
            headers: headersJson,
            body: bodyBytes,
            completedAt: DateTimeOffset.UtcNow);

        using var connection = new TestDbConnection(
            onExecuteNonQuery: _ => 0,
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-completed");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-completed",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result.IsReplay.Should().BeTrue();
        result.CachedResponse.Should().NotBeNull();
        result.CachedResponse!.StatusCode.Should().Be(201);
        result.CachedResponse.Headers.Should().ContainKey("X-Header");
        result.CachedResponse.Body.ToArray().Should().BeEquivalentTo(bodyBytes);
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenCompletedWithNullValues_UsesDefaults()
    {
        var existingRow = CreateRow(
            status: 2,
            fingerprint: "fp-nulls",
            statusCode: null,
            headers: null,
            body: null,
            completedAt: DateTimeOffset.UtcNow);

        using var connection = new TestDbConnection(
            onExecuteNonQuery: _ => 0,
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-nulls");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-nulls",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result.CachedResponse.Should().NotBeNull();
        result.CachedResponse!.StatusCode.Should().Be(200);
        result.CachedResponse.Headers.Should().BeEmpty();
        result.CachedResponse.Body.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenProcessingWithActiveLease_ReturnsInFlightConflict()
    {
        var existingRow = CreateRow(
            status: 1,
            fingerprint: "fp-inflight",
            leaseExpires: DateTimeOffset.UtcNow.AddMinutes(10));

        using var connection = new TestDbConnection(
            onExecuteNonQuery: cmd => cmd.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ? 0 : 99,
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-inflight");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-inflight",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenStealingExpiredLease_ReturnsAcquiredStale()
    {
        var existingRow = CreateRow(
            status: 1,
            fingerprint: "fp-stale",
            leaseExpires: DateTimeOffset.UtcNow.AddMinutes(-5));

        using var connection = new TestDbConnection(
            onExecuteNonQuery: cmd => cmd.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-stale");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-stale",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.AcquiredStale);
        result.IsAcquired.Should().BeTrue();
        result.ConcurrencyVersion.Should().Be(2); // 1 + 1
        result.OwnerToken.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquireCoreAsync_WhenStealFails_ReturnsInFlightConflict()
    {
        var existingRow = CreateRow(
            status: 3, // Failed
            fingerprint: "fp-steal-fail",
            leaseExpires: DateTimeOffset.UtcNow.AddMinutes(-5));

        using var connection = new TestDbConnection(
            onExecuteNonQuery: _ => 0, // No row updated
            onExecuteReader: _ => new TestDbDataReader(new List<Dictionary<string, object?>> { existingRow }));

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-key-steal-fail");

        var result = await OracleIdempotencyStore.TryAcquireCoreAsync(
            connection,
            null,
            tenantId,
            "orders",
            key,
            "fp-steal-fail",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromDays(7));

        result.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task MarkCompletedAsync_TransactionalOverload_ExecutesOnConnection()
    {
        var store = new OracleIdempotencyStore(DummyConnectionString);

        using var connection = new TestDbConnection(onExecuteNonQuery: _ => 1);
        using var transaction = connection.BeginTransaction();

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-tx-key");
        var ownerToken = Guid.NewGuid();
        var headers = new Dictionary<string, string[]> { ["X-Header"] = new[] { "V1" } };

        var resultSuccess = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            ownerToken,
            concurrencyVersion: 1,
            statusCode: 200,
            headers: headers,
            responseBody: new byte[] { 1, 2, 3 },
            retentionDuration: TimeSpan.FromDays(1),
            connection: connection,
            transaction: transaction);

        resultSuccess.Should().BeTrue();

        using var failConnection = new TestDbConnection(onExecuteNonQuery: _ => 0);
        var resultFail = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            ownerToken,
            1,
            200,
            new Dictionary<string, string[]>(),
            ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromDays(1),
            connection: failConnection,
            transaction: null);

        resultFail.Should().BeFalse();
    }

    [Fact]
    public async Task MarkFailedAsync_TransactionalOverload_ExecutesOnConnection()
    {
        var store = new OracleIdempotencyStore(DummyConnectionString);

        using var connection = new TestDbConnection(onExecuteNonQuery: _ => 1);
        using var transaction = connection.BeginTransaction();

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("oracle-fail-key");
        var ownerToken = Guid.NewGuid();

        var resultSuccess = await store.MarkFailedAsync(
            tenantId,
            "orders",
            key,
            ownerToken,
            concurrencyVersion: 1,
            connection: connection,
            transaction: transaction);

        resultSuccess.Should().BeTrue();

        using var failConnection = new TestDbConnection(onExecuteNonQuery: _ => 0);
        var resultFail = await store.MarkFailedAsync(
            tenantId,
            "orders",
            key,
            ownerToken,
            concurrencyVersion: 1,
            connection: failConnection,
            transaction: null);

        resultFail.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredRecordsCoreAsync_ExecutesAndReturnsAffectedRows()
    {
        using var connection = new TestDbConnection(onExecuteNonQuery: _ => 7);
        var count = await OracleIdempotencyStore.CleanupExpiredRecordsCoreAsync(
            connection,
            null,
            DateTimeOffset.UtcNow,
            10);

        count.Should().Be(7);
    }

    [Fact]
    public void OracleRecordDto_GettersAndSetters_Roundtrip()
    {
        var dto = new OracleIdempotencyStore.OracleRecordDto
        {
            Id = Guid.NewGuid().ToByteArray(),
            TenantId = Guid.NewGuid().ToByteArray(),
            Scope = "scope-oracle",
            IdempotencyKey = "key-oracle",
            Fingerprint = "fp-oracle",
            Status = 2,
            OwnerToken = Guid.NewGuid().ToByteArray(),
            ConcurrencyVersion = 1,
            ResponseStatusCode = 200,
            ResponseHeaders = "{}",
            ResponseBody = new byte[] { 1 },
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            RetentionExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        dto.Scope.Should().Be("scope-oracle");
        dto.IdempotencyKey.Should().Be("key-oracle");
        dto.Fingerprint.Should().Be("fp-oracle");
        dto.Status.Should().Be(2);
        dto.ResponseStatusCode.Should().Be(200);
    }

    private static Dictionary<string, object?> CreateRow(
        byte status,
        string fingerprint,
        int? statusCode = null,
        string? headers = null,
        byte[]? body = null,
        DateTimeOffset? leaseExpires = null,
        DateTimeOffset? completedAt = null)
    {
        return new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid().ToByteArray(),
            ["TenantId"] = Guid.NewGuid().ToByteArray(),
            ["Scope"] = "orders",
            ["IdempotencyKey"] = "key-test",
            ["Fingerprint"] = fingerprint,
            ["Status"] = status,
            ["OwnerToken"] = Guid.NewGuid().ToByteArray(),
            ["ConcurrencyVersion"] = 1,
            ["ResponseStatusCode"] = (object?)statusCode ?? DBNull.Value,
            ["ResponseHeaders"] = (object?)headers ?? DBNull.Value,
            ["ResponseBody"] = (object?)body ?? DBNull.Value,
            ["CreatedAtUtc"] = DateTimeOffset.UtcNow,
            ["LeaseExpiresAtUtc"] = (object?)(leaseExpires ?? DateTimeOffset.UtcNow.AddMinutes(5)) ?? DBNull.Value,
            ["CompletedAtUtc"] = (object?)completedAt ?? DBNull.Value,
            ["RetentionExpiresAtUtc"] = DateTimeOffset.UtcNow.AddDays(7)
        };
    }
}

internal sealed class TestDbException : DbException
{
    public TestDbException(string message) : base(message) { }
}

internal sealed class TestDbConnection : DbConnection
{
    private readonly Func<TestDbCommand, int> _onExecuteNonQuery;
    private readonly Func<TestDbCommand, object?> _onExecuteScalar;
    private readonly Func<TestDbCommand, DbDataReader> _onExecuteReader;

    public TestDbConnection(
        Func<TestDbCommand, int>? onExecuteNonQuery = null,
        Func<TestDbCommand, object?>? onExecuteScalar = null,
        Func<TestDbCommand, DbDataReader>? onExecuteReader = null)
    {
        _onExecuteNonQuery = onExecuteNonQuery ?? (_ => 1);
        _onExecuteScalar = onExecuteScalar ?? (_ => null);
        _onExecuteReader = onExecuteReader ?? (_ => new TestDbDataReader(new List<Dictionary<string, object?>>()));
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new TestDbTransaction(this);
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "test";
    public override ConnectionState State => ConnectionState.Open;
    public override string ServerVersion => "1.0";
    public override string DataSource => "test";
    protected override DbCommand CreateDbCommand() => new TestDbCommand(_onExecuteNonQuery, _onExecuteScalar, _onExecuteReader);
}

internal sealed class TestDbCommand : DbCommand
{
    private readonly Func<TestDbCommand, int> _onExecuteNonQuery;
    private readonly Func<TestDbCommand, object?> _onExecuteScalar;
    private readonly Func<TestDbCommand, DbDataReader> _onExecuteReader;

    public TestDbCommand(
        Func<TestDbCommand, int> onExecuteNonQuery,
        Func<TestDbCommand, object?> onExecuteScalar,
        Func<TestDbCommand, DbDataReader> onExecuteReader)
    {
        _onExecuteNonQuery = onExecuteNonQuery;
        _onExecuteScalar = onExecuteScalar;
        _onExecuteReader = onExecuteReader;
    }

    public override void Cancel() { }
    public override int ExecuteNonQuery() => _onExecuteNonQuery(this);
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(_onExecuteNonQuery(this));
    public override object? ExecuteScalar() => _onExecuteScalar(this);
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => Task.FromResult(_onExecuteScalar(this));
    public override void Prepare() { }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new TestDbParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }
    public override bool DesignTimeVisible { get; set; }
    protected override DbParameter CreateDbParameter() => new TestDbParameter();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _onExecuteReader(this);
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => Task.FromResult(_onExecuteReader(this));
}

internal sealed class TestDbTransaction : DbTransaction
{
    private readonly DbConnection _connection;
    public TestDbTransaction(DbConnection connection) => _connection = connection;
    public override void Commit() { }
    public override void Rollback() { }
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    protected override DbConnection? DbConnection => _connection;
}

internal sealed class TestDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override object? Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
    public override void ResetDbType() { }
}

internal sealed class TestDbParameterCollection : DbParameterCollection
{
    private readonly List<object?> _parameters = new();
    public override int Add(object? value) { _parameters.Add(value); return _parameters.Count - 1; }
    public override void AddRange(Array values) { foreach (var v in values) _parameters.Add(v); }
    public override void Clear() => _parameters.Clear();
    public override bool Contains(object? value) => _parameters.Contains(value);
    public override int IndexOf(object? value) => _parameters.IndexOf(value);
    public override void Insert(int index, object? value) => _parameters.Insert(index, value);
    public override bool IsFixedSize => false;
    public override bool IsReadOnly => false;
    public override bool IsSynchronized => false;
    public override void Remove(object? value) => _parameters.Remove(value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName) { }
    public override object SyncRoot => this;
    public override int Count => _parameters.Count;
    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    protected override DbParameter GetParameter(int index) => (DbParameter)_parameters[index]!;
    protected override DbParameter GetParameter(string parameterName) => (DbParameter)_parameters[0]!;
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) { }
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) => _parameters.ToArray().CopyTo(array, index);
    public override int IndexOf(string parameterName) => -1;
}

internal sealed class TestDbDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object?>> _rows;
    private int _currentRow = -1;

    public TestDbDataReader(List<Dictionary<string, object?>> rows) => _rows = rows;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        _currentRow++;
        return _currentRow < _rows.Count;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

    public override object GetValue(int ordinal) => _rows[_currentRow].Values.ElementAt(ordinal) ?? DBNull.Value;
    public override string GetName(int ordinal) => _rows[_currentRow].Keys.ElementAt(ordinal);
    public override int GetOrdinal(string name) => _rows[_currentRow].Keys.ToList().FindIndex(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
    public override int FieldCount => _rows.Count > 0 ? _rows[0].Count : 0;
    public override bool HasRows => _rows.Count > 0;
    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => _rows.Count;
    public override bool NextResult() => false;
    public override System.Collections.IEnumerator GetEnumerator() => _rows.GetEnumerator();

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "text";
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override Type GetFieldType(int ordinal) => GetValue(ordinal)?.GetType() ?? typeof(object);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override int GetValues(object[] values) => 0;
}
