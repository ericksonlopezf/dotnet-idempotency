# Asynchronous Messaging & At-Least-Once Delivery

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. At-Least-Once Delivery & Message Duplication

Distributed message brokers (Kafka, RabbitMQ, Azure Service Bus, Amazon SQS) operate under **at-least-once delivery** guarantees. Network blips during broker acknowledgments cause messages to be re-delivered.

```text
Broker ──► Deliver MessageId: "MSG-100" ──► Consumer executes ──► Acknowledgment lost!
Broker ──► Redeliver MessageId: "MSG-100" ──► Consumer receives duplicate!
```

---

## 2. Message Consumer Idempotency Pattern

Using `EricksonLopez.Idempotency`, message consumers can wrap processing logic using the unique `MessageId`:

```csharp
public sealed class OrderCreatedConsumer
{
    private readonly IdempotencyEngine _engine;
    private readonly IShippingService _shippingService;

    public OrderCreatedConsumer(IdempotencyEngine engine, IShippingService shippingService)
    {
        _engine = engine;
        _shippingService = shippingService;
    }

    public async Task ConsumeAsync(MessageContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        var key = new IdempotencyKey(context.MessageId);
        var fingerprint = IdempotencyFingerprintHasher.Compute(
            "Consume",
            "OrderCreatedEvent",
            context.TenantId.ToString(),
            null,
            context.RawBodyBytes);

        await _engine.ExecuteAsync(
            tenantId: context.TenantId,
            scope: "OrderCreatedConsumer",
            key: key,
            fingerprint: fingerprint,
            operation: async ct =>
            {
                await _shippingService.ScheduleShipmentAsync(context.Message.OrderId, ct);
                return true;
            },
            cancellationToken: cancellationToken);
    }
}
```

---

## 3. Benefits over Naive Deduplication

1. **Deterministic Execution**: Handles concurrent redeliveries across multiple competing consumer instances.
2. **Lease Protection**: If a worker crashes mid-consumption, a replacement worker can safely claim the lease after expiration.
3. **Outbox Cohesion**: Interoperates seamlessly with Outbox patterns.
