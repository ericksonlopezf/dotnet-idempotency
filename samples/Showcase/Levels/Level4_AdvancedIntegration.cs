// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Mediator;
using EricksonLopez.Idempotency.Result;
using EricksonLopez.Idempotency.Testing;
using EricksonLopez.Mediator;
using EricksonLopez.Result;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates advanced ecosystem integration: Result pattern error mapping (all three error factories),
/// AsErrorResult extension method, Mediator IdempotencyPipelineBehavior, and ITransactionalIdempotencyStore
/// atomic Outbox + Idempotency pattern.
/// </summary>
public sealed class Level4AdvancedIntegration : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 4 — Advanced Ecosystem Integration (Result & Mediator)";

    /// <inheritdoc/>
    public string Description => "IdempotencyErrors (all factories), AsErrorResult<T>(), IdempotencyPipelineBehavior, ITransactionalIdempotencyStore atomic Outbox pattern.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── 1. IdempotencyErrors — all three factory methods ─────────────────────
        Console.WriteLine("1. EricksonLopez.Result — IdempotencyErrors factory methods:");

        var conflictError = IdempotencyErrors.InFlightConflict("TX-001");
        var mismatchError = IdempotencyErrors.FingerprintMismatch("TX-001");
        var leaseLostError = IdempotencyErrors.LeaseLost("TX-001");

        Console.WriteLine($" -> InFlightConflict:    Code='{conflictError.Code}', Type={conflictError.Type}");
        Console.WriteLine($" -> FingerprintMismatch: Code='{mismatchError.Code}', Type={mismatchError.Type}");
        Console.WriteLine($" -> LeaseLost:           Code='{leaseLostError.Code}', Type={leaseLostError.Type}");

        // ─── 2. AsErrorResult<T>() — IdempotencyClaimResult extension ────────────
        Console.WriteLine("\n2. IdempotencyResultExtensions.AsErrorResult<T>() — mapping claim results to Result<T>:");

        var store0 = new InMemoryIdempotencyStore();
        var key0 = new IdempotencyKey("RESULT-KEY-001");
        var fp0 = IdempotencyFingerprintHasher.Compute("POST", "scope0", Guid.Empty.ToString(), null, System.Text.Encoding.UTF8.GetBytes("{\"x\":1}"));

        // Acquire once
        _ = await store0.TryAcquireAsync(Guid.Empty, "scope0", key0, fp0, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        // Simulate fingerprint mismatch
        var mismatchedFp = IdempotencyFingerprintHasher.Compute("POST", "scope0", Guid.Empty.ToString(), null, System.Text.Encoding.UTF8.GetBytes("{\"x\":999}"));
        var mismatchClaim = await store0.TryAcquireAsync(Guid.Empty, "scope0", key0, mismatchedFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        var errorResult = mismatchClaim.AsErrorResult<TransferFundsResult>("RESULT-KEY-001");
        Console.WriteLine($" -> AsErrorResult on FingerprintMismatch: IsFailure={errorResult?.IsFailure}, Error={errorResult?.Error.Code}");

        // Simulate in-flight conflict: two concurrent acquires
        var store1 = new InMemoryIdempotencyStore();
        var key1 = new IdempotencyKey("RESULT-KEY-002");
        var fp1 = IdempotencyFingerprintHasher.Compute("POST", "scope1", Guid.Empty.ToString(), null, System.Text.Encoding.UTF8.GetBytes("{\"y\":1}"));
        _ = await store1.TryAcquireAsync(Guid.Empty, "scope1", key1, fp1, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        var conflictClaim = await store1.TryAcquireAsync(Guid.Empty, "scope1", key1, fp1, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        var conflictResult = conflictClaim.AsErrorResult<TransferFundsResult>("RESULT-KEY-002");
        Console.WriteLine($" -> AsErrorResult on InFlightConflict:   IsFailure={conflictResult?.IsFailure}, Error={conflictResult?.Error.Code}");

        // ─── 3. IdempotencyPipelineBehavior ──────────────────────────────────────
        Console.WriteLine("\n3. EricksonLopez.Mediator IdempotencyPipelineBehavior<TRequest, TResponse>:");

        var store = new InMemoryIdempotencyStore();
        var options = new IdempotencyOptions();
        var policy = new DefaultIdempotencyPolicy(options);
        var serializer = new SystemTextJsonIdempotencySerializer();

        var pipelineBehavior = new IdempotencyPipelineBehavior<TransferFundsCommand, TransferFundsResult>(
            store, serializer, policy);

        int handlerInvocations = 0;
        var nextDelegate = new TransferNext(() =>
        {
            handlerInvocations++;
            return new TransferFundsResult("TX-MEDIATOR-1", 250.00m);
        });

        var command = new TransferFundsCommand("ACC-1", "ACC-2", 250.00m, "MEDIATOR-KEY-777");

        Console.WriteLine(" -> Sending TransferFundsCommand through Mediator pipeline (Call 1)...");
        var res1 = await pipelineBehavior.Handle(command, nextDelegate, CancellationToken.None);
        Console.WriteLine($"    TransactionId: {res1.TransactionId}, Handler Invocations: {handlerInvocations}");

        Console.WriteLine(" -> Sending TransferFundsCommand through Mediator pipeline (Call 2 - Replay)...");
        var res2 = await pipelineBehavior.Handle(command, nextDelegate, CancellationToken.None);
        Console.WriteLine($"    TransactionId: {res2.TransactionId}, Handler Invocations: {handlerInvocations}");

        if (handlerInvocations == 1)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" -> [SUCCESS] Mediator pipeline guaranteed single-execution!");
            Console.ResetColor();
        }

        // ─── 4. AddMediatorIdempotency() — DI registration ───────────────────────
        Console.WriteLine("\n4. AddMediatorIdempotency() — how to register via DI:");
        Console.WriteLine(@"
   services.AddMediatorIdempotency();
   // Internally: services.AddIdempotencyCore()
   //             services.TryAddTransient(typeof(IPipelineBehavior<,>),
   //                                      typeof(IdempotencyPipelineBehavior<,>));
   //
   // All commands implementing IIdempotentRequest are automatically guarded.
");

        // ─── 5. ITransactionalIdempotencyStore — atomic Outbox pattern ───────────
        Console.WriteLine("5. ITransactionalIdempotencyStore — atomic Outbox + Idempotency pattern:");
        Console.WriteLine(@"
   // Sequence: BEGIN TX → Domain Mutation → Outbox Event → MarkCompleted → COMMIT TX

   await using var conn = await dataSource.OpenConnectionAsync(ct);
   await using var tx = await conn.BeginTransactionAsync(ct);

   // 1. Domain operation (e.g. Insert order)
   await domainRepository.SaveAsync(order, conn, tx, ct);

   // 2. Outbox event (dual-write prevention)
   await outboxWriter.WriteAsync(orderPlacedEvent, conn, tx, ct);

   // 3. Mark idempotency as completed within the SAME transaction:
   if (store is ITransactionalIdempotencyStore txStore)
   {
       await txStore.MarkCompletedAsync(
           tenantId, scope, key, ownerToken, version,
           statusCode, headers, body, retention,
           conn, tx, ct);
   }

   await tx.CommitAsync(ct);

   // Supported in: PostgreSqlIdempotencyStore, SqlServerIdempotencyStore,
   //               OracleIdempotencyStore, MySqlIdempotencyStore,
   //               MariaDbIdempotencyStore, SqliteIdempotencyStore.
");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] All Level 4 integrations demonstrated.");
        Console.ResetColor();
    }

    /// <summary>
    /// Represents a mock pipeline next delegate for funds transfer.
    /// </summary>
    public readonly struct TransferNext : INext<TransferFundsResult>
    {
        private readonly Func<TransferFundsResult> _handler;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferNext"/> struct.
        /// </summary>
        /// <param name="handler">The handler delegate to invoke.</param>
        public TransferNext(Func<TransferFundsResult> handler)
        {
            _handler = handler;
        }

        /// <inheritdoc/>
        public ValueTask<TransferFundsResult> InvokeAsync() => new ValueTask<TransferFundsResult>(_handler());
    }

    /// <summary>
    /// Represents a command for transferring funds between accounts.
    /// </summary>
    /// <param name="FromAccount">The source account number.</param>
    /// <param name="ToAccount">The target account number.</param>
    /// <param name="Amount">The amount to transfer.</param>
    /// <param name="Key">The idempotency key string.</param>
    public sealed record TransferFundsCommand(string FromAccount, string ToAccount, decimal Amount, string Key) : IIdempotentRequest
    {
        /// <inheritdoc/>
        public IdempotencyKey IdempotencyKey => new IdempotencyKey(Key);

        /// <inheritdoc/>
        /// <remarks>In multi-tenant systems, resolve TenantId from the current tenant context (e.g., EricksonLopez.MultiTenancy.ITenantContext).</remarks>
        public Guid TenantId => Guid.Empty;
    }

    /// <summary>
    /// Represents the result of a funds transfer operation.
    /// </summary>
    /// <param name="TransactionId">The unique transaction identifier.</param>
    /// <param name="Amount">The transferred amount.</param>
    public sealed record TransferFundsResult(string TransactionId, decimal Amount);
}
