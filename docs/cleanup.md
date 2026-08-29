# TTL, Expiration & Retention Cleanup Strategies

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Storage Growth & Unbounded Tables

Every completed idempotent operation retains a serialized response payload for a configurable duration (`DefaultRetentionDuration`, e.g. 7 to 30 days).

Without automated purging, tables will experience unbounded disk growth, index bloat, and degraded B-Tree traversal performance.

---

## 2. Low-Impact Batch Deletion

Never execute unbounded deletes like `DELETE FROM idempotency_records WHERE retention_expires_at_utc < NOW()`. Large deletes lock the table, bloat WAL logs, and stall active transactions.

`EricksonLopez.Idempotency` provides batched purging via `CleanupExpiredRecordsAsync`:

```csharp
public sealed class IdempotencyCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IdempotencyCleanupBackgroundService> _logger;

    public IdempotencyCleanupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<IdempotencyCleanupBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

                var totalPurged = 0;
                int purgedInBatch;
                do
                {
                    purgedInBatch = await store.CleanupExpiredRecordsAsync(
                        DateTimeOffset.UtcNow,
                        batchSize: 1000,
                        stoppingToken);

                    totalPurged += purgedInBatch;
                } while (purgedInBatch >= 1000 && !stoppingToken.IsCancellationRequested);

                if (totalPurged > 0)
                {
                    _logger.LogInformation("Purged {Count} expired idempotency records.", totalPurged);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during idempotency record cleanup.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

---

## 3. Database Table Partitioning (Optional for Massive Scale)

For systems processing tens of millions of requests daily, partition `idempotency_records` by range on `retention_expires_at_utc`:

```sql
CREATE TABLE idempotency_records_partitioned
(
    -- column definitions...
) PARTITION BY RANGE (retention_expires_at_utc);
```
Old partitions can be instantly dropped via `DROP TABLE idempotency_records_2026_01;` with zero IO overhead and zero WAL bloat.
