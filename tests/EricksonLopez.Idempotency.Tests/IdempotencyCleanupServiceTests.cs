// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EricksonLopez.Idempotency.Tests;

public sealed class IdempotencyCleanupServiceTests
{
    [Fact]
    public void Options_DefaultsAndSetters_WorkCorrectly()
    {
        var options = new IdempotencyCleanupOptions();
        options.Interval.Should().Be(TimeSpan.FromHours(1));
        options.BatchSize.Should().Be(1000);

        options.Interval = TimeSpan.FromMinutes(10);
        options.BatchSize = 250;

        options.Interval.Should().Be(TimeSpan.FromMinutes(10));
        options.BatchSize = 250;
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new IdempotencyCleanupOptions();

        var act1 = () => new IdempotencyCleanupBackgroundService(null!, options, new TestLogger<IdempotencyCleanupBackgroundService>());
        act1.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");

        var act2 = () => new IdempotencyCleanupBackgroundService(scopeFactory, null!, new TestLogger<IdempotencyCleanupBackgroundService>());
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");

        var act3 = () => new IdempotencyCleanupBackgroundService(scopeFactory, options, null!);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task StartAsync_And_StopAsync_LogsStartAndStop()
    {
        var services = new ServiceCollection();
        var testStore = new InMemoryIdempotencyStore();
        services.AddSingleton<IIdempotencyStore>(testStore);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var options = new IdempotencyCleanupOptions
        {
            Interval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10
        };

        var logger = new TestLogger<IdempotencyCleanupBackgroundService>();
        var service = new IdempotencyCleanupBackgroundService(scopeFactory, options, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(30);
        await service.StopAsync(CancellationToken.None);

        logger.LoggedEntries.Should().Contain(e => e.Message.Contains("Idempotency cleanup service started"));
        logger.LoggedEntries.Should().Contain(e => e.Message.Contains("Idempotency cleanup service stopped"));
    }

    [Fact]
    public async Task ExecuteAsync_RunsCleanupCycles_PurgedRecordsAndNothingToClean_AndLogsDetails()
    {
        var services = new ServiceCollection();
        var testStore = new InMemoryIdempotencyStore();
        services.AddSingleton<IIdempotencyStore>(testStore);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        // Create an expired record in store
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("exp-key");
        var claim = await testStore.TryAcquireAsync(tenantId, "test", key, "fp", TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        await testStore.MarkCompletedAsync(tenantId, "test", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromMilliseconds(10));

        await Task.Delay(50); // Ensure expiration

        var options = new IdempotencyCleanupOptions
        {
            Interval = TimeSpan.FromMilliseconds(15),
            BatchSize = 100
        };

        var logger = new TestLogger<IdempotencyCleanupBackgroundService>();
        var service = new IdempotencyCleanupBackgroundService(scopeFactory, options, logger);

        await service.StartAsync(CancellationToken.None);

        // Wait for multiple cycles (one with purged > 0, then one with purged == 0)
        await Task.Delay(100);

        await service.StopAsync(CancellationToken.None);

        logger.LoggedEntries.Should().Contain(e => e.Message.Contains("purged 1 expired record(s)"));
        logger.LoggedEntries.Should().Contain(e => e.Message.Contains("no expired records found"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoreThrowsException_HandlesGracefully_AndLogsCycleError()
    {
        var services = new ServiceCollection();
        var failingStore = new FailingCleanupStore();
        services.AddSingleton<IIdempotencyStore>(failingStore);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var options = new IdempotencyCleanupOptions
        {
            Interval = TimeSpan.FromMilliseconds(15),
            BatchSize = 50
        };

        var logger = new TestLogger<IdempotencyCleanupBackgroundService>();
        var service = new IdempotencyCleanupBackgroundService(scopeFactory, options, logger);

        await service.StartAsync(CancellationToken.None);

        await Task.Delay(70);

        await service.StopAsync(CancellationToken.None);

        logger.LoggedEntries.Should().Contain(e => e.LogLevel == LogLevel.Error && e.Message.Contains("Idempotency cleanup cycle failed unexpectedly"));
    }

    [Fact]
    public async Task ExecuteAsync_WithLongInterval_DoesNotRunCleanupPrematurely()
    {
        var services = new ServiceCollection();
        var trackingStore = new TrackingCleanupStore();
        services.AddSingleton<IIdempotencyStore>(trackingStore);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var options = new IdempotencyCleanupOptions
        {
            Interval = TimeSpan.FromHours(1),
            BatchSize = 50
        };

        var logger = new TestLogger<IdempotencyCleanupBackgroundService>();
        var service = new IdempotencyCleanupBackgroundService(scopeFactory, options, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(40);
        await service.StopAsync(CancellationToken.None);

        trackingStore.CleanupCallCount.Should().Be(0);
    }

    private sealed class TrackingCleanupStore : IIdempotencyStore
    {
        public int CleanupCallCount { get; private set; }
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default)
        {
            CleanupCallCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class FailingCleanupStore : IIdempotencyStore
    {
        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkCompletedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, int statusCode, IReadOnlyDictionary<string, string[]> headers, ReadOnlyMemory<byte> responseBody, TimeSpan retentionDuration, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> MarkFailedAsync(Guid tenantId, string scope, IdempotencyKey key, Guid ownerToken, int concurrencyVersion, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated database timeout during cleanup");
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
}
