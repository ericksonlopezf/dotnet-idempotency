// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Encapsulates configuration options for the idempotency cleanup background service.
/// </summary>
public sealed class IdempotencyCleanupOptions
{
    /// <summary>
    /// Gets or sets the interval between consecutive cleanup cycles.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of expired records to purge per cleanup cycle.
    /// </summary>
    /// <remarks>Limiting the batch size prevents long-running deletes from impacting concurrent store operations.</remarks>
    public int BatchSize { get; set; } = 1000;
}

/// <summary>
/// Provides a background service that periodically purges expired idempotency records from the store.
/// </summary>
/// <remarks>
/// <para>
/// Register this service by calling <c>services.AddIdempotencyCleanupService()</c> in your DI setup.
/// The service uses <see cref="IIdempotencyStore.CleanupExpiredRecordsAsync"/> internally.
/// </para>
/// <para>
/// This service is registered as an <see cref="IHostedService"/> and runs on the host background
/// task scheduler. It will not interfere with request processing.
/// </para>
/// </remarks>
internal sealed partial class IdempotencyCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdempotencyCleanupOptions _options;
    private readonly ILogger<IdempotencyCleanupBackgroundService> _logger;

    public IdempotencyCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        IdempotencyCleanupOptions options,
        ILogger<IdempotencyCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger, _options.Interval, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCycleError(_logger, ex);
            }
        }

        LogStopped(_logger);
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        var purged = await store.CleanupExpiredRecordsAsync(
            DateTimeOffset.UtcNow,
            _options.BatchSize,
            cancellationToken).ConfigureAwait(false);

        if (purged > 0)
            LogPurged(_logger, purged);
        else
            LogNothingToClean(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency cleanup service started. Interval: {Interval}, BatchSize: {BatchSize}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval, int batchSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency cleanup service stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency cleanup cycle failed unexpectedly.")]
    private static partial void LogCycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency cleanup: purged {Count} expired record(s).")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Idempotency cleanup: no expired records found.")]
    private static partial void LogNothingToClean(ILogger logger);
}
