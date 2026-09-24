# Asynchronous Messaging & At-Least-Once Delivery

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. At-Least-Once Delivery & Competing Consumer Hazards

Modern message brokers (Kafka, RabbitMQ, Azure Service Bus, Amazon SQS) operate under **at-least-once delivery** guarantees. In distributed microservices, network timeouts, broker ack drops, consumer rebalances, or consumer crash-restarts inevitably lead to redelivered messages:

```text
┌─────────────────┐       Deliver MessageId: "MSG-100"       ┌──────────────────┐
│                 ├─────────────────────────────────────────►│  Consumer Pod A  │
│                 │                                          │  (Executes OK)   │
│                 │◄─── Network Partition (ACK Lost!) ───────┤  (Crash / Drops) │
│ Message Broker  │                                          └──────────────────┘
│ (RabbitMQ / ASB)│
│                 │       Redeliver MessageId: "MSG-100"     ┌──────────────────┐
│                 ├─────────────────────────────────────────►│  Consumer Pod B  │
│                 │                                          │ (Duplicate Run!) │
└─────────────────┘                                          └──────────────────┘
```

Without an architectural idempotency barrier, consuming the duplicate message causes duplicate shipments, double billing, or corrupted domain state.

---

## 2. The Dual-Write Hazard in Asynchronous Consumers

When a message consumer attempts naive deduplication (e.g. checking a cache or writing to a separate store after committing the business transaction), it falls victim to the **dual-write problem**:

1. **False Execution / Duplicate Charge**: The domain transaction commits, but the consumer crashes before acknowledging or updating the deduplication store. The message is re-delivered, and the consumer processes the charge again.
2. **False Completion / Ghost Acknowledgement**: The deduplication record is written first, but the database transaction rolls back. A retry occurs, detects the deduplication record, and skips the operation—losing the business update forever.

---

## 3. Direct Message Consumer Pattern with `IdempotencyEngine`

For operations that communicate with external non-transactional downstream APIs (e.g. third-party fulfillment services), use `IdempotencyEngine` to guard execution:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;

public sealed class ShippingNotificationConsumer
{
    private readonly IdempotencyEngine _engine;
    private readonly IShippingGateway _shippingGateway;

    public ShippingNotificationConsumer(IdempotencyEngine engine, IShippingGateway shippingGateway)
    {
        _engine = engine;
        _shippingGateway = shippingGateway;
    }

    public async Task ConsumeAsync(MessageEnvelope<ShipmentDispatchedEvent> envelope, CancellationToken cancellationToken)
    {
        var key = new IdempotencyKey(envelope.MessageId);
        var fingerprint = IdempotencyFingerprintHasher.Compute(
            "Consume",
            "ShippingNotification",
            envelope.TenantId.ToString(),
            null,
            envelope.RawPayloadBytes);

        var result = await _engine.ExecuteAsync(
            tenantId: envelope.TenantId,
            scope: "ShippingConsumer",
            key: key,
            fingerprint: fingerprint,
            operation: async ct =>
            {
                await _shippingGateway.NotifyCourierAsync(envelope.Message.TrackingNumber, ct);
                return true;
            },
            cancellationToken: cancellationToken);

        if (result.IsReplay)
        {
            // Message was previously processed; acknowledge safely to broker
            return;
        }
    }
}

public sealed record MessageEnvelope<T>(Guid TenantId, string MessageId, byte[] RawPayloadBytes, T Message);
public sealed record ShipmentDispatchedEvent(string TrackingNumber);
public interface IShippingGateway { Task NotifyCourierAsync(string trackingNumber, CancellationToken ct); }
```

---

## 4. Atomic Transactional Consumer Pattern with `ITransactionalIdempotencyStore`

When the consumer mutates internal database state and publishes domain/integration events, use `ITransactionalIdempotencyStore` (implemented by PostgreSQL, SQL Server, MySQL, MariaDB, and Oracle providers). This ensures that **message deduplication, domain mutation, and the Transactional Outbox commit in a single ACID transaction**:

```csharp
using System;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EricksonLopez.Idempotency;

public sealed class ProcessPaymentConsumer
{
    private readonly ITransactionalIdempotencyStore _idempotencyStore;
    private readonly DbDataSource _dataSource; // NpgsqlDataSource, SqlConnection, etc.

    public ProcessPaymentConsumer(
        ITransactionalIdempotencyStore idempotencyStore,
        DbDataSource dataSource)
    {
        _idempotencyStore = idempotencyStore;
        _dataSource = dataSource;
    }

    public async Task ProcessAsync(MessageEnvelope<OrderPaymentCommand> envelope, CancellationToken cancellationToken)
    {
        var key = new IdempotencyKey(envelope.MessageId);
        var fingerprint = IdempotencyFingerprintHasher.Compute(
            "Consume",
            "ProcessPayment",
            envelope.TenantId.ToString(),
            null,
            envelope.RawPayloadBytes);

        // 1. Acquire lease outside transaction
        var claim = await _idempotencyStore.TryAcquireAsync(
            tenantId: envelope.TenantId,
            scope: "Payments",
            key: key,
            fingerprint: fingerprint,
            leaseDuration: TimeSpan.FromSeconds(30),
            retentionDuration: TimeSpan.FromDays(7),
            cancellationToken: cancellationToken);

        if (claim.IsCompleted)
        {
            // Already processed and committed earlier. Safe broker ACK!
            return;
        }

        if (claim.IsConflict)
        {
            // Another worker currently holds active lease. Throw to trigger broker redelivery with backoff.
            throw new InvalidOperationException($"Concurrent processing in flight for message: {envelope.MessageId}");
        }

        // 2. Open connection and begin single atomic ACID transaction
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            // 3. Perform domain state mutation
            await connection.ExecuteAsync(
                @"UPDATE orders 
                     SET payment_status = 'Captured', updated_at_utc = @Now 
                   WHERE order_id = @OrderId AND tenant_id = @TenantId",
                new { Now = DateTimeOffset.UtcNow, OrderId = envelope.Message.OrderId, TenantId = envelope.TenantId },
                transaction: transaction);

            // 4. Stage Outbox event for downstream publication
            await connection.ExecuteAsync(
                @"INSERT INTO outbox_messages (id, tenant_id, event_type, payload, created_at_utc) 
                  VALUES (@Id, @TenantId, @EventType, @Payload, @CreatedAtUtc)",
                new
                {
                    Id = Guid.NewGuid(),
                    TenantId = envelope.TenantId,
                    EventType = "OrderPaymentCapturedEvent",
                    Payload = JsonSerializer.Serialize(new { envelope.Message.OrderId, envelope.Message.Amount }),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                transaction: transaction);

            // 5. Mark idempotency record COMPLETED within the SAME database transaction
            var marked = await _idempotencyStore.MarkCompletedAsync(
                tenantId: envelope.TenantId,
                scope: "Payments",
                key: key,
                ownerToken: claim.OwnerToken,
                concurrencyVersion: claim.ConcurrencyVersion,
                statusCode: 200,
                headers: null,
                responseBody: ReadOnlyMemory<byte>.Empty,
                retentionDuration: TimeSpan.FromDays(7),
                connection: connection,
                transaction: transaction,
                cancellationToken: cancellationToken);

            if (!marked)
            {
                throw new DBConcurrencyException("Failed to commit idempotency record: lease stolen by zombie worker recovery.");
            }

            // 6. Commit all operations atomically
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);

            // Mark lease failed so subsequent retries are immediate
            await _idempotencyStore.MarkFailedAsync(
                tenantId: envelope.TenantId,
                scope: "Payments",
                key: key,
                ownerToken: claim.OwnerToken,
                concurrencyVersion: claim.ConcurrencyVersion,
                cancellationToken: cancellationToken);

            throw;
        }
    }
}

public sealed record OrderPaymentCommand(string OrderId, decimal Amount);
```

---

## 5. MassTransit Consumer Integration Pattern

When integrating with [MassTransit](https://masstransit.io), encapsulate the idempotency claim inside a custom `IFilter<ConsumeContext<T>>` or directly in the consumer:

```csharp
using System;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using MassTransit;

public sealed class MassTransitIdempotencyFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly IIdempotencyStore _store;

    public MassTransitIdempotencyFilter(IIdempotencyStore store)
    {
        _store = store;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var messageId = context.MessageId?.ToString() ?? Guid.NewGuid().ToString("N");
        var key = new IdempotencyKey(messageId);
        var tenantId = context.Headers.Get<Guid?>("TenantId") ?? Guid.Empty;

        var fingerprint = IdempotencyFingerprintHasher.Compute(
            "MassTransit",
            typeof(T).Name,
            tenantId.ToString(),
            null,
            System.Text.Encoding.UTF8.GetBytes(messageId));

        var claim = await _store.TryAcquireAsync(
            tenantId: tenantId,
            scope: typeof(T).Name,
            key: key,
            fingerprint: fingerprint,
            leaseDuration: TimeSpan.FromSeconds(45),
            retentionDuration: TimeSpan.FromDays(7),
            cancellationToken: context.CancellationToken);

        if (claim.IsCompleted)
        {
            // Already processed; short-circuit pipeline to acknowledge message
            return;
        }

        if (claim.IsConflict)
        {
            // In-flight by competing instance; defer processing with retry
            throw new ConcurrencyException($"Message {messageId} is currently being processed by another consumer.");
        }

        try
        {
            await next.Send(context);

            await _store.MarkCompletedAsync(
                tenantId: tenantId,
                scope: typeof(T).Name,
                key: key,
                ownerToken: claim.OwnerToken,
                concurrencyVersion: claim.ConcurrencyVersion,
                statusCode: 200,
                headers: null,
                responseBody: ReadOnlyMemory<byte>.Empty,
                retentionDuration: TimeSpan.FromDays(7),
                cancellationToken: context.CancellationToken);
        }
        catch
        {
            await _store.MarkFailedAsync(
                tenantId: tenantId,
                scope: typeof(T).Name,
                key: key,
                ownerToken: claim.OwnerToken,
                concurrencyVersion: claim.ConcurrencyVersion,
                cancellationToken: context.CancellationToken);

            throw;
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("idempotency");
}
```

---

## 6. Wolverine Message Handler Integration Pattern

For [Wolverine](https://wolverine.netlify.app) message pipelines, middleware methods intercept the command or message context:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;

public static class WolverineIdempotencyMiddleware
{
    public static async Task<HandlerContinuation> BeforeAsync(
        IIdempotencyStore store,
        IdempotentMessage message,
        CancellationToken cancellationToken)
    {
        var key = new IdempotencyKey(message.MessageId);
        var claim = await store.TryAcquireAsync(
            tenantId: message.TenantId,
            scope: "Wolverine",
            key: key,
            fingerprint: message.ComputeFingerprint(),
            leaseDuration: TimeSpan.FromSeconds(30),
            retentionDuration: TimeSpan.FromDays(7),
            cancellationToken: cancellationToken);

        if (claim.IsCompleted)
        {
            // Stop handler execution and acknowledge message
            return HandlerContinuation.Stop;
        }

        if (claim.IsConflict)
        {
            // Triggers Wolverine retry policy with jitter
            throw new ConcurrencyException("Message lease is currently locked by a competing node.");
        }

        return HandlerContinuation.Continue;
    }
}

public enum HandlerContinuation { Continue, Stop }
public abstract record IdempotentMessage(Guid TenantId, string MessageId)
{
    public abstract string ComputeFingerprint();
}
```

---

## 7. Poison Messages & Lease Expiration Mechanics

1. **Worker Crash Recovery**: If a consumer crashes while processing a message, its lease expires after `leaseDuration` (e.g. 30 seconds). When the broker redelivers the unacknowledged message to another consumer pod, `TryAcquireAsync` detects that `lease_expires_at_utc < @Now` and safely steals the lease, incrementing `concurrency_version`.
2. **Zombie Prevention**: If the crashed worker resumes unexpectedly, any attempt to commit via `MarkCompletedAsync` fails because its cached `concurrency_version` does not match the database.
3. **Dead Letter Queue (DLQ)**: If a message repeatedly fails business validation, `MarkFailedAsync` releases the lease, and the broker's retry limit routes the message to the DLQ.

---

## Related References

- [Transactional Store Participation](transaction-integration.md)
- [Lease Ownership & Fencing Tokens](leases.md)
- [Formal State Machine](state-machine.md)
- [ADR-011: Transactional Store Participation](adr/adr-011-transactional-store-participation.md)
