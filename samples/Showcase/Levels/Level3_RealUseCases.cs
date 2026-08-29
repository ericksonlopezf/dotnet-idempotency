// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates real-world use cases: payload fingerprint validation, policy evaluation,
/// IdempotencyStatus enum lifecycle, CachedIdempotencyResponse access, and IdempotencyProblemDetails.
/// </summary>
public sealed class Level3RealUseCases : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 3 — Real Use Cases, Status Lifecycle & Problem Details";

    /// <inheritdoc/>
    public string Description => "Fingerprint mismatch detection, IdempotencyStatus transitions, CachedIdempotencyResponse, IdempotencyProblemDetails, and DefaultIdempotencyPolicy evaluation.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── Scenario 1: Payment Gateway — fingerprint mismatch detection ─────────
        Console.WriteLine("Scenario 1: Payment Processing Gateway — fingerprint mismatch detection");

        var store = new InMemoryIdempotencyStore();
        var options = new IdempotencyOptions();
        var policy = new DefaultIdempotencyPolicy(options);
        var serializer = new SystemTextJsonIdempotencySerializer();
        var contextAccessor = new AsyncLocalIdempotencyContextAccessor();
        var engine = new IdempotencyEngine(store, policy, serializer, contextAccessor, NullLogger<IdempotencyEngine>.Instance);

        var idempotencyKey = new IdempotencyKey("PAYMENT-TX-001");
        var tenantId = Guid.NewGuid();

        // 1. Initial legitimate payment of $100.00
        var legitimatePayload = Encoding.UTF8.GetBytes("{\"accountId\":\"ACC-99\",\"amount\":100.00,\"currency\":\"USD\"}");
        var legitimateFp = IdempotencyFingerprintHasher.Compute("POST", "payments", tenantId.ToString(), null, legitimatePayload);

        Console.WriteLine("\n1. Executing legitimate payment of $100.00...");
        var result1 = await engine.ExecuteAsync(tenantId, "payments", idempotencyKey, legitimateFp, async ct =>
        {
            await Task.Delay(10, ct);
            return new PaymentResult("TX-100", 100.00m, "Authorized");
        });
        Console.WriteLine($" -> Result: {result1.Status}, AuthCode: {result1.TransactionId}");

        // 2. Tampered payment attempt ($5,000.00 reusing PAYMENT-TX-001)
        var tamperedPayload = Encoding.UTF8.GetBytes("{\"accountId\":\"ACC-99\",\"amount\":5000.00,\"currency\":\"USD\"}");
        var tamperedFp = IdempotencyFingerprintHasher.Compute("POST", "payments", tenantId.ToString(), null, tamperedPayload);

        Console.WriteLine("\n2. Attempting tampered payment of $5,000.00 with the SAME Idempotency-Key...");
        try
        {
            await engine.ExecuteAsync(tenantId, "payments", idempotencyKey, tamperedFp, async ct =>
            {
                await Task.Delay(10, ct);
                return new PaymentResult("TX-5000", 5000.00m, "Authorized");
            });
            throw new InvalidOperationException("Security failure: Fingerprint mismatch was not detected!");
        }
        catch (IdempotencyFingerprintMismatchException ex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($" -> [BLOCKED] IdempotencyFingerprintMismatchException caught.");
            Console.ResetColor();
            Console.WriteLine($"    Key:              {ex.Key}");
            Console.WriteLine($"    ExpectedFingerprint: {ex.ExpectedFingerprint?[..16]}...");
            Console.WriteLine($"    ActualFingerprint:   {ex.ActualFingerprint?[..16]}...");
        }

        // ─── Scenario 2: IdempotencyStatus enum lifecycle ─────────────────────────
        Console.WriteLine("\nScenario 2: IdempotencyStatus enum — lifecycle states");
        Console.WriteLine($" -> Processing  ({(byte)IdempotencyStatus.Processing}): Operation is actively executing under a lease.");
        Console.WriteLine($" -> Completed   ({(byte)IdempotencyStatus.Completed}): Operation succeeded; result is immutable and cached.");
        Console.WriteLine($" -> Failed      ({(byte)IdempotencyStatus.Failed}):    Operation failed; eligible for retry depending on policy.");

        // ─── Scenario 3: CachedIdempotencyResponse access via TryAcquireAsync ────
        Console.WriteLine("\nScenario 3: CachedIdempotencyResponse — accessing cached response after completion");
        var store2 = new InMemoryIdempotencyStore();
        var key2 = new IdempotencyKey("REPLAY-KEY-001");
        var fp2 = IdempotencyFingerprintHasher.Compute("POST", "invoices", "tenant-X", null, Encoding.UTF8.GetBytes("{\"invoiceId\":\"INV-01\"}"));

        // First acquisition — execute
        var claim1 = await store2.TryAcquireAsync(Guid.Empty, "invoices", key2, fp2, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($" -> First acquire: Status={claim1.Status}, OwnerToken={claim1.OwnerToken?.ToString()[..8]}...");

        // Mark completed with a fake response body
        var responseBody = serializer.Serialize(new PaymentResult("INV-01-RESULT", 500m, "Paid"));
        var marked = await store2.MarkCompletedAsync(
            Guid.Empty, "invoices", key2,
            claim1.OwnerToken!.Value, claim1.ConcurrencyVersion!.Value,
            200,
            new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["X-Idempotency-Key"] = ["REPLAY-KEY-001"]
            },
            responseBody,
            TimeSpan.FromDays(7));
        Console.WriteLine($" -> MarkCompleted: {marked}");

        // Second acquisition — should return CachedIdempotencyResponse
        var claim2 = await store2.TryAcquireAsync(Guid.Empty, "invoices", key2, fp2, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($" -> Second acquire: Status={claim2.Status}, IsReplay={claim2.IsReplay}");

        if (claim2.CachedResponse is { } cached)
        {
            Console.WriteLine($"    CachedResponse.StatusCode: {cached.StatusCode}");
            Console.WriteLine($"    CachedResponse.Headers count: {cached.Headers.Count}");
            Console.WriteLine($"    CachedResponse.Body size: {cached.Body.Length} bytes");
            var deserialized = serializer.Deserialize<PaymentResult>(cached.Body);
            Console.WriteLine($"    Deserialized: TransactionId={deserialized?.TransactionId}, Amount={deserialized?.Amount}");
        }

        // ─── Scenario 4: DefaultIdempotencyPolicy evaluation ──────────────────────
        Console.WriteLine("\nScenario 4: DefaultIdempotencyPolicy — IsCacheableStatusCode evaluation");
        Console.WriteLine($" -> Is 200 OK cacheable:                 {policy.IsCacheableStatusCode(200)} (cached for future replay)");
        Console.WriteLine($" -> Is 201 Created cacheable:            {policy.IsCacheableStatusCode(201)} (cached for future replay)");
        Console.WriteLine($" -> Is 204 No Content cacheable:         {policy.IsCacheableStatusCode(204)} (cached for future replay)");
        Console.WriteLine($" -> Is 400 Bad Request cacheable:        {policy.IsCacheableStatusCode(400)} (client error — not cached)");
        Console.WriteLine($" -> Is 500 Internal Error cacheable:     {policy.IsCacheableStatusCode(500)} (transient — allows retry)");
        Console.WriteLine($" -> Is 503 Service Unavailable cacheable:{policy.IsCacheableStatusCode(503)} (transient — allows retry)");
        Console.WriteLine($" -> AllowRetryOnFailure:                 {policy.AllowRetryOnFailure}");
        Console.WriteLine($" -> LeaseDuration:                       {policy.LeaseDuration.TotalSeconds}s");
        Console.WriteLine($" -> RetentionDuration:                   {policy.RetentionDuration.TotalDays} days");

        // ─── Scenario 5: IdempotencyProblemDetails (RFC 9110 problem details) ────
        Console.WriteLine("\nScenario 5: IdempotencyProblemDetails — RFC 9110 problem detail payload");

        var conflictProblem = new IdempotencyProblemDetails(
            Type: "https://docs.ericksonlopez.com/idempotency/errors/in-flight-conflict",
            Title: "In-Flight Conflict",
            Status: 409,
            Detail: "An identical operation with idempotency key 'PAYMENT-TX-001' is currently being processed.");

        Console.WriteLine($" -> Type:   {conflictProblem.Type}");
        Console.WriteLine($" -> Title:  {conflictProblem.Title}");
        Console.WriteLine($" -> Status: {conflictProblem.Status}");
        Console.WriteLine($" -> Detail: {conflictProblem.Detail}");

        var mismatchProblem = new IdempotencyProblemDetails(
            Type: "https://docs.ericksonlopez.com/idempotency/errors/fingerprint-mismatch",
            Title: "Fingerprint Mismatch",
            Status: 422,
            Detail: "The idempotency key 'PAYMENT-TX-001' was previously used with a different request payload.");

        Console.WriteLine($"\n -> Mismatch Status: {mismatchProblem.Status}, Title: {mismatchProblem.Title}");
    }

    /// <summary>
    /// Represents the sample payment authorization outcome.
    /// </summary>
    /// <param name="TransactionId">The transaction identifier.</param>
    /// <param name="Amount">The payment amount.</param>
    /// <param name="Status">The payment authorization status.</param>
    public sealed record PaymentResult(string TransactionId, decimal Amount, string Status);
}
