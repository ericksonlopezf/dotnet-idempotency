// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.Idempotency.Tests;

public sealed class IdempotencyEngineTests
{
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly IdempotencyOptions _options = new();
    private readonly DefaultIdempotencyPolicy _policy;
    private readonly SystemTextJsonIdempotencySerializer _serializer = new();
    private readonly TrackingContextAccessor _accessor = new();
    private readonly TestLogger<IdempotencyEngine> _testLogger = new();
    private readonly IdempotencyEngine _engine;

    public IdempotencyEngineTests()
    {
        _policy = new DefaultIdempotencyPolicy(_options);
        _engine = new IdempotencyEngine(_store, _policy, _serializer, _accessor, _testLogger);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var actStore = () => new IdempotencyEngine(null!, _policy, _serializer, _accessor, NullLogger<IdempotencyEngine>.Instance);
        actStore.Should().Throw<ArgumentNullException>().WithParameterName("store");

        var actPolicy = () => new IdempotencyEngine(_store, null!, _serializer, _accessor, NullLogger<IdempotencyEngine>.Instance);
        actPolicy.Should().Throw<ArgumentNullException>().WithParameterName("policy");

        var actSerializer = () => new IdempotencyEngine(_store, _policy, null!, _accessor, NullLogger<IdempotencyEngine>.Instance);
        actSerializer.Should().Throw<ArgumentNullException>().WithParameterName("serializer");

        var actAccessor = () => new IdempotencyEngine(_store, _policy, _serializer, null!, NullLogger<IdempotencyEngine>.Instance);
        actAccessor.Should().Throw<ArgumentNullException>().WithParameterName("contextAccessor");

        var actLogger = () => new IdempotencyEngine(_store, _policy, _serializer, _accessor, null!);
        actLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_NullOperation_ThrowsArgumentNullException()
    {
        var act = () => _engine.ExecuteAsync<TestResponse>(Guid.NewGuid(), "orders", new IdempotencyKey("k1"), "fp-1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_FirstExecution_ComputesResultAndCaches_AndEmitsMetrics()
    {
        var measurements = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (inst, ml) =>
            {
                if (inst.Meter.Name == IdempotencyDiagnostics.ServiceName) ml.EnableMeasurementEvents(inst);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, _, _, _) => measurements.Add(inst.Name));
        meterListener.Start();

        var inspectingStore = new InspectingStore();
        var engine = new IdempotencyEngine(inspectingStore, _policy, _serializer, _accessor, _testLogger);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("order-101");
        var executionCount = 0;

        var result1 = await engine.ExecuteAsync(tenantId, "orders", key, "fp-1", _ =>
        {
            executionCount++;
            _accessor.IdempotencyContext.Should().NotBeNull();
            _accessor.IdempotencyContext!.Key.Should().Be(key);
            _accessor.IdempotencyContext.TenantId.Should().Be(tenantId);
            return Task.FromResult(new TestResponse("Order #101 Created"));
        });

        // After completion, context should be reset to null
        _accessor.IdempotencyContext.Should().BeNull();
        _accessor.SetHistory.Should().EndWith((IdempotencyContext?)null);

        inspectingStore.LastStatusCode.Should().Be(200);
        inspectingStore.LastRetention.Should().Be(_policy.RetentionDuration);
        inspectingStore.LastOwnerToken.Should().NotBeNull();
        inspectingStore.LastConcurrencyVersion.Should().NotBeNull();

        measurements.Should().Contain("idempotency.requests");
        measurements.Should().Contain("idempotency.executions");
        measurements.Should().Contain("idempotency.completed");

        var result2 = await engine.ExecuteAsync(tenantId, "orders", key, "fp-1", _ =>
        {
            executionCount++;
            return Task.FromResult(new TestResponse("Order #101 Created"));
        });

        executionCount.Should().Be(1);
        result1.Message.Should().Be("Order #101 Created");
        result2.Message.Should().Be("Order #101 Created");
        measurements.Should().Contain("idempotency.duplicates");
        measurements.Should().Contain("idempotency.replayed");
    }

    [Fact]
    public async Task ExecuteAsync_FingerprintMismatch_ThrowsException_AndRecordsMetrics()
    {
        var measurements = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (inst, ml) =>
            {
                if (inst.Meter.Name == IdempotencyDiagnostics.ServiceName) ml.EnableMeasurementEvents(inst);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, _, _, _) => measurements.Add(inst.Name));
        meterListener.Start();

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("order-102");

        await _engine.ExecuteAsync(tenantId, "orders", key, "fp-original", _ => Task.FromResult(new TestResponse("Success")));

        var act = () => _engine.ExecuteAsync(tenantId, "orders", key, "fp-tampered", _ => Task.FromResult(new TestResponse("Success")));

        var ex = await act.Should().ThrowAsync<IdempotencyFingerprintMismatchException>();
        ex.Which.Key.Should().Be(key.Value);
        ex.Which.ExpectedFingerprint.Should().Be("fp-original");
        ex.Which.ActualFingerprint.Should().Be("fp-tampered");

        measurements.Should().Contain("idempotency.fingerprint_mismatch");
    }

    [Fact]
    public async Task ExecuteAsync_InFlightConflict_ThrowsConflictException_AndRecordsMetrics()
    {
        var measurements = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (inst, ml) =>
            {
                if (inst.Meter.Name == IdempotencyDiagnostics.ServiceName) ml.EnableMeasurementEvents(inst);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, _, _, _) => measurements.Add(inst.Name));
        meterListener.Start();

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("order-conflict");

        await _store.TryAcquireAsync(tenantId, "orders", key, "fp-flight", TimeSpan.FromMinutes(5), TimeSpan.FromDays(1));

        var act = () => _engine.ExecuteAsync(tenantId, "orders", key, "fp-flight", _ => Task.FromResult(new TestResponse("Success")));

        var ex = await act.Should().ThrowAsync<IdempotencyConflictException>();
        ex.Which.Key.Should().Be(key.Value);

        measurements.Should().Contain("idempotency.duplicates");
        measurements.Should().Contain("idempotency.conflicts");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLeaseLostBeforeCompletion_LogsWarningAndReturnsResult()
    {
        var customStore = new MockFailingCompleteStore();
        var logger = new TestLogger<IdempotencyEngine>();
        var engine = new IdempotencyEngine(customStore, _policy, _serializer, _accessor, logger);

        var key = new IdempotencyKey("lease-lost-key");
        var result = await engine.ExecuteAsync(Guid.NewGuid(), "orders", key, "fp-1", _ => Task.FromResult(new TestResponse("OK")));

        result.Message.Should().Be("OK");
        logger.LoggedEntries.Should().Contain(e => e.LogLevel == LogLevel.Warning && e.Message.Contains("Idempotency lease was lost"));
    }

    [Fact]
    public async Task ExecuteAsync_OnFailure_MarksRecordFailed_AndRecordsMetrics()
    {
        var measurements = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (inst, ml) =>
            {
                if (inst.Meter.Name == IdempotencyDiagnostics.ServiceName) ml.EnableMeasurementEvents(inst);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, _, _, _) => measurements.Add(inst.Name));
        meterListener.Start();

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("order-fail");

        var act = () => _engine.ExecuteAsync<TestResponse>(tenantId, "orders", key, "fp-fail", _ => throw new InvalidOperationException("Business failure"));
        await act.Should().ThrowAsync<InvalidOperationException>();

        _accessor.IdempotencyContext.Should().BeNull();
        _accessor.SetHistory.Should().EndWith((IdempotencyContext?)null);
        measurements.Should().Contain("idempotency.failed");

        var retryResult = await _engine.ExecuteAsync(tenantId, "orders", key, "fp-fail", _ => Task.FromResult(new TestResponse("Recovered")));
        retryResult.Message.Should().Be("Recovered");
    }

    [Fact]
    public async Task ExecuteAsync_WhenClaimIsAcquiredNewWithCachedResponse_DoesNotReplayAndExecutes()
    {
        var customStore = new MockAcquiredWithCachedResponseStore();
        var engine = new IdempotencyEngine(customStore, _policy, _serializer, _accessor, _testLogger);

        var key = new IdempotencyKey("acquired-with-cached");
        var executed = false;
        var result = await engine.ExecuteAsync(Guid.NewGuid(), "orders", key, "fp-1", _ =>
        {
            executed = true;
            _accessor.IdempotencyContext!.IsReplay.Should().BeFalse();
            return Task.FromResult(new TestResponse("Executed"));
        });

        executed.Should().BeTrue();
        result.Message.Should().Be("Executed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationFailsWithOnlyOwnerToken_RethrowsWithoutMarkFailed()
    {
        var customStore = new MockAcquireWithTokenOnlyStore();
        var logger = new TestLogger<IdempotencyEngine>();
        var engine = new IdempotencyEngine(customStore, _policy, _serializer, _accessor, logger);

        var key = new IdempotencyKey("token-only-fail");
        var act = () => engine.ExecuteAsync<TestResponse>(Guid.NewGuid(), "orders", key, "fp-1", _ => throw new InvalidOperationException("Fail token only"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Fail token only");

        _accessor.IdempotencyContext.Should().BeNull();
        logger.LoggedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationFailsWithOnlyVersion_RethrowsWithoutMarkFailed()
    {
        var customStore = new MockAcquireWithVersionOnlyStore();
        var logger = new TestLogger<IdempotencyEngine>();
        var engine = new IdempotencyEngine(customStore, _policy, _serializer, _accessor, logger);

        var key = new IdempotencyKey("version-only-fail");
        var act = () => engine.ExecuteAsync<TestResponse>(Guid.NewGuid(), "orders", key, "fp-1", _ => throw new InvalidOperationException("Fail version only"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Fail version only");

        _accessor.IdempotencyContext.Should().BeNull();
        logger.LoggedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationFailsWithoutOwnerToken_RethrowsWithoutMarkFailed()
    {
        var customStore = new MockAcquireWithoutTokenStore();
        var logger = new TestLogger<IdempotencyEngine>();
        var engine = new IdempotencyEngine(customStore, _policy, _serializer, _accessor, logger);

        var key = new IdempotencyKey("no-token-fail");
        var act = () => engine.ExecuteAsync<TestResponse>(Guid.NewGuid(), "orders", key, "fp-1", _ => throw new InvalidOperationException("Fail without token"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Fail without token");

        _accessor.IdempotencyContext.Should().BeNull();
        logger.LoggedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenMarkFailedThrowsException_SuppressesAndLogsError()
    {
        var customStore = new MockFailingMarkFailedStore();
        var logger = new TestLogger<IdempotencyEngine>();
        var engine = new IdempotencyEngine(customStore, _policy, _serializer, _accessor, logger);

        var key = new IdempotencyKey("fail-mark-key");
        var act = () => engine.ExecuteAsync<TestResponse>(Guid.NewGuid(), "orders", key, "fp-1", _ => throw new InvalidOperationException("Initial business error"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Initial business error");

        logger.LoggedEntries.Should().Contain(e => e.LogLevel == LogLevel.Error && e.Message.Contains("Failed to mark idempotency record as Failed"));
    }

    [Fact]
    public async Task ExecuteAsync_WithActiveActivityListener_RecordsAllActivityTelemetry()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == IdempotencyDiagnostics.ServiceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("act-key-1");

        // 1. Success path
        var res1 = await _engine.ExecuteAsync(tenantId, "billing", key, "fp-act", _ => Task.FromResult(new TestResponse("OK")));
        res1.Message.Should().Be("OK");

        // 2. Replay path
        var res2 = await _engine.ExecuteAsync(tenantId, "billing", key, "fp-act", _ => Task.FromResult(new TestResponse("Ignored")));
        res2.Message.Should().Be("OK");

        // 3. Fingerprint mismatch path
        var actMismatch = () => _engine.ExecuteAsync(tenantId, "billing", key, "fp-diff", _ => Task.FromResult(new TestResponse("Fail")));
        await actMismatch.Should().ThrowAsync<IdempotencyFingerprintMismatchException>();

        // 4. In-flight conflict path
        var conflictKey = new IdempotencyKey("act-conflict");
        await _store.TryAcquireAsync(tenantId, "billing", conflictKey, "fp-act", TimeSpan.FromMinutes(5), TimeSpan.FromDays(1));
        var actConflict = () => _engine.ExecuteAsync(tenantId, "billing", conflictKey, "fp-act", _ => Task.FromResult(new TestResponse("Fail")));
        await actConflict.Should().ThrowAsync<IdempotencyConflictException>();

        // 5. Operation failure path
        var failKey = new IdempotencyKey("act-fail");
        var actFail = () => _engine.ExecuteAsync<TestResponse>(tenantId, "billing", failKey, "fp-act", _ => throw new InvalidOperationException("Activity error"));
        await actFail.Should().ThrowAsync<InvalidOperationException>();

        activities.Should().HaveCount(5);

        var successAct = activities[0];
        successAct.OperationName.Should().Be("Idempotency.Execute");
        successAct.GetTagItem("idempotency.scope").Should().Be("billing");
        successAct.GetTagItem("idempotency.tenant_id").Should().Be(tenantId.ToString());

        var replayAct = activities[1];
        replayAct.GetTagItem("idempotency.replayed").Should().Be(true);

        var mismatchAct = activities[2];
        mismatchAct.Status.Should().Be(ActivityStatusCode.Error);
        mismatchAct.StatusDescription.Should().Be("Fingerprint mismatch");

        var conflictAct = activities[3];
        conflictAct.Status.Should().Be(ActivityStatusCode.Error);
        conflictAct.StatusDescription.Should().Be("In-flight conflict");

        var failAct = activities[4];
        failAct.Status.Should().Be(ActivityStatusCode.Error);
        failAct.StatusDescription.Should().Be("Activity error");
    }

    private sealed class InspectingStore : IIdempotencyStore
    {
        private readonly InMemoryIdempotencyStore _inner = new();
        public int? LastStatusCode { get; private set; }
        public TimeSpan? LastRetention { get; private set; }
        public Guid? LastOwnerToken { get; private set; }
        public int? LastConcurrencyVersion { get; private set; }

        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            return _inner.TryAcquireAsync(tenantId, scope, key, fingerprint, leaseDuration, retentionDuration, cancellationToken);
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            LastStatusCode = statusCode;
            LastRetention = retentionDuration;
            LastOwnerToken = ownerToken;
            LastConcurrencyVersion = concurrencyVersion;
            return _inner.MarkCompletedAsync(tenantId, scope, key, ownerToken, concurrencyVersion, statusCode, headers, responseBody, retentionDuration, cancellationToken);
        }

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default)
        {
            return _inner.MarkFailedAsync(tenantId, scope, key, ownerToken, concurrencyVersion, cancellationToken);
        }

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default)
        {
            return _inner.CleanupExpiredRecordsAsync(utcNow, batchSize, cancellationToken);
        }
    }

    private sealed class MockAcquireWithoutTokenStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, null, null, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Should not be called!");

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class MockFailingCompleteStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, null, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class MockFailingMarkFailedStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, null, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Storage connectivity lost during MarkFailed");
        }

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class MockAcquiredWithCachedResponseStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            var fakeCached = new CachedIdempotencyResponse(200, new Dictionary<string, string[]>(), new byte[] { 1 });
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, fakeCached, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class MockAcquireWithTokenOnlyStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), null, null, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Should not be called!");

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class MockAcquireWithVersionOnlyStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, 1, null, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Should not be called!");

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class TrackingContextAccessor : IIdempotencyContextAccessor
    {
        private IdempotencyContext? _context;
        public List<IdempotencyContext?> SetHistory { get; } = new();

        public IdempotencyContext? IdempotencyContext
        {
            get => _context;
            set
            {
                _context = value;
                SetHistory.Add(value);
            }
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message)> LoggedEntries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LoggedEntries.Add((logLevel, formatter(state, exception)));
        }
    }

    public sealed record TestResponse(string Message);
}
