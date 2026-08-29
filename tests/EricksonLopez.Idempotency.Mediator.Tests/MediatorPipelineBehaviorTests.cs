// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Mediator;
using EricksonLopez.Idempotency.Testing;
using EricksonLopez.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Idempotency.Mediator.Tests;

public sealed class MediatorPipelineBehaviorTests
{
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly IIdempotencySerializer _serializer = new SystemTextJsonIdempotencySerializer();
    private readonly IIdempotencyPolicy _policy = new DefaultIdempotencyPolicy(new IdempotencyOptions
    {
        DefaultLeaseDuration = TimeSpan.FromMinutes(2),
        DefaultRetentionDuration = TimeSpan.FromDays(7)
    });

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var actStore = () => new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(null!, _serializer, _policy);
        actStore.Should().Throw<ArgumentNullException>().WithParameterName("store");

        var actSerializer = () => new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(_store, null!, _policy);
        actSerializer.Should().Throw<ArgumentNullException>().WithParameterName("serializer");

        var actPolicy = () => new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(_store, _serializer, null!);
        actPolicy.Should().Throw<ArgumentNullException>().WithParameterName("policy");
    }

    [Fact]
    public async Task Handle_FirstTimeExecution_RunsHandlerAndCachesResponse()
    {
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(_store, _serializer, _policy);
        var command = new TestCommand(new IdempotencyKey("cmd-first-run"), "FirstRunData");
        var executionCount = 0;

        var next = new TestNext<TestResponseDto>(() =>
        {
            executionCount++;
            return new ValueTask<TestResponseDto>(new TestResponseDto(200, "Created"));
        });

        var result1 = await behavior.Handle(command, next, CancellationToken.None);
        var result2 = await behavior.Handle(command, next, CancellationToken.None);

        executionCount.Should().Be(1);
        result1.Should().BeEquivalentTo(new TestResponseDto(200, "Created"));
        result2.Should().BeEquivalentTo(new TestResponseDto(200, "Created"));
    }

    [Fact]
    public async Task Handle_MultiTenantCommand_UsesCommandTenantIdInStoreAndFingerprint()
    {
        var recordingStore = new RecordingMockStore();
        var behavior = new IdempotencyPipelineBehavior<MultiTenantCommand, TestResponseDto>(recordingStore, _serializer, _policy);
        var tenant = Guid.NewGuid();
        var command = new MultiTenantCommand(tenant, new IdempotencyKey("cmd-tenant"), "TenantPayload");

        var next = new TestNext<TestResponseDto>(() => ValueTask.FromResult(new TestResponseDto(200, "TenantOK")));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.Should().BeEquivalentTo(new TestResponseDto(200, "TenantOK"));
        recordingStore.LastTenantId.Should().Be(tenant);
        recordingStore.LastScope.Should().Be(typeof(MultiTenantCommand).FullName);
        recordingStore.LastLeaseDuration.Should().Be(TimeSpan.FromMinutes(2));
        recordingStore.LastRetentionDuration.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task Handle_WhenFingerprintMismatch_ThrowsIdempotencyFingerprintMismatchException()
    {
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(_store, _serializer, _policy);

        var cmd1 = new TestCommand(new IdempotencyKey("cmd-mismatch"), "PayloadOriginal");
        var next1 = new TestNext<TestResponseDto>(() => ValueTask.FromResult(new TestResponseDto(200, "OK")));
        await behavior.Handle(cmd1, next1, CancellationToken.None);

        var cmd2 = new TestCommand(new IdempotencyKey("cmd-mismatch"), "PayloadModified");
        var next2 = new TestNext<TestResponseDto>(() => ValueTask.FromResult(new TestResponseDto(200, "OK")));

        var act = () => behavior.Handle(cmd2, next2, CancellationToken.None).AsTask();

        var ex = await act.Should().ThrowAsync<IdempotencyFingerprintMismatchException>();
        ex.Which.Key.Should().Be("cmd-mismatch");
        ex.Which.ExpectedFingerprint.Should().NotBeNullOrWhiteSpace();
        ex.Which.ActualFingerprint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Handle_WhenInFlightConflict_ThrowsIdempotencyConflictException()
    {
        var mockStore = new InFlightConflictMockStore();
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(mockStore, _serializer, _policy);
        var command = new TestCommand(new IdempotencyKey("cmd-inflight"), "Data");

        var next = new TestNext<TestResponseDto>(() => ValueTask.FromResult(new TestResponseDto(200, "OK")));
        var act = () => behavior.Handle(command, next, CancellationToken.None).AsTask();

        var ex = await act.Should().ThrowAsync<IdempotencyConflictException>();
        ex.Which.Key.Should().Be("cmd-inflight");
    }

    [Fact]
    public async Task Handle_WhenCompletedReplayWithNullCachedResponse_ReturnsDefault()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, null, "fp"));
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto?>(mockStore, _serializer, _policy);
        var command = new TestCommand(new IdempotencyKey("cmd-null-cached"), "Data");

        var next = new TestNext<TestResponseDto?>(() => ValueTask.FromResult<TestResponseDto?>(new TestResponseDto(200, "OK")));
        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenAcquiredNewWithNonNullCachedResponse_ExecutesNextAndDoesNotReplay()
    {
        var cached = new CachedIdempotencyResponse(200, new Dictionary<string, string[]>(), _serializer.Serialize(new TestResponseDto(999, "Cached")));
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, cached, "fp"));
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(mockStore, _serializer, _policy);
        var command = new TestCommand(new IdempotencyKey("cmd-acquired-with-cached"), "Data");
        var executionCount = 0;

        var next = new TestNext<TestResponseDto>(() =>
        {
            executionCount++;
            return ValueTask.FromResult(new TestResponseDto(200, "Fresh"));
        });

        var result = await behavior.Handle(command, next, CancellationToken.None);

        executionCount.Should().Be(1);
        result.Should().BeEquivalentTo(new TestResponseDto(200, "Fresh"));
    }

    [Fact]
    public async Task Handle_WhenNextThrowsException_MarksFailedAndRethrows()
    {
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(_store, _serializer, _policy);
        var command = new TestCommand(new IdempotencyKey("cmd-exception"), "FailingData");

        var failingNext = new TestNext<TestResponseDto>(() => throw new InvalidOperationException("Mediator pipeline failure"));

        var act = () => behavior.Handle(command, failingNext, CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Mediator pipeline failure");

        // Retrying with same key succeeds since it was marked failed
        var successNext = new TestNext<TestResponseDto>(() => ValueTask.FromResult(new TestResponseDto(200, "Recovered")));
        var retryResult = await behavior.Handle(command, successNext, CancellationToken.None);

        retryResult.Should().BeEquivalentTo(new TestResponseDto(200, "Recovered"));
    }

    [Fact]
    public async Task Handle_WhenExceptionAndClaimHasNullOwnerToken_DoesNotThrowInvalidOperation()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, 1, null, null));
        var behavior = new IdempotencyPipelineBehavior<TestCommand, TestResponseDto>(mockStore, _serializer, _policy);
        var command = new TestCommand(new IdempotencyKey("cmd-null-owner-token"), "Data");

        var failingNext = new TestNext<TestResponseDto>(() => throw new InvalidOperationException("OriginalFailure"));
        var act = () => behavior.Handle(command, failingNext, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("OriginalFailure");
    }

    [Fact]
    public void AddMediatorIdempotency_NullServices_ThrowsArgumentNullException()
    {
        var act = () => MediatorServiceCollectionExtensions.AddMediatorIdempotency(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMediatorIdempotency_RegistersRequiredServicesAndResolvesPipelineBehavior()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        var result = services.AddMediatorIdempotency();
        result.Should().BeSameAs(services);

        var provider = services.BuildServiceProvider();
        var policy = provider.GetService<IIdempotencyPolicy>();
        var serializer = provider.GetService<IIdempotencySerializer>();
        var behavior = provider.GetService<IPipelineBehavior<TestCommand, TestResponseDto>>();

        policy.Should().NotBeNull();
        serializer.Should().NotBeNull();
        behavior.Should().NotBeNull();
        behavior.Should().BeOfType<IdempotencyPipelineBehavior<TestCommand, TestResponseDto>>();
    }

    public sealed record TestCommand(IdempotencyKey IdempotencyKey, string Data) : IIdempotentRequest
    {
        public Guid TenantId => Guid.Empty;
    }

    public sealed record MultiTenantCommand(Guid TenantId, IdempotencyKey IdempotencyKey, string Payload) : IIdempotentRequest;

    public sealed record TestResponseDto(int Code, string Status);

    private readonly struct TestNext<T> : INext<T>
    {
        private readonly Func<ValueTask<T>> _func;
        public TestNext(Func<ValueTask<T>> func) => _func = func;
        public ValueTask<T> InvokeAsync() => _func();
    }

    private sealed class RecordingMockStore : IIdempotencyStore
    {
        public Guid? LastTenantId { get; private set; }
        public string? LastScope { get; private set; }
        public TimeSpan? LastLeaseDuration { get; private set; }
        public TimeSpan? LastRetentionDuration { get; private set; }

        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            LastScope = scope;
            LastLeaseDuration = leaseDuration;
            LastRetentionDuration = retentionDuration;
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, null, null));
        }

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class StaticClaimMockStore : IIdempotencyStore
    {
        private readonly IdempotencyClaimResult _claim;
        public StaticClaimMockStore(IdempotencyClaimResult claim) => _claim = claim;

        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(_claim);

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class InFlightConflictMockStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, fingerprint));

        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
