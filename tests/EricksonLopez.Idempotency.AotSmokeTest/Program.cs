// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.AotSmokeTest;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("=================================================");
Console.WriteLine(" EricksonLopez.Idempotency NativeAOT Test Suite ");
Console.WriteLine("=================================================");

int passedTests = 0;

void Assert([DoesNotReturnIf(false)] bool condition, string testName)
{
    if (!condition)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {testName}");
        Console.ResetColor();
        throw new InvalidOperationException($"Assertion failed for: {testName}");
    }

    passedTests++;
    Console.WriteLine($"[PASS] {testName}");
}

// ── 1. IdempotencyKey Tests ──────────────────────────────────────────────────
Console.WriteLine("\n--- 1. IdempotencyKey Invariants ---");

var key1 = new IdempotencyKey("KEY-12345");
var key2 = IdempotencyKey.Create("KEY-12345");
var key3 = new IdempotencyKey("KEY-99999");

Assert(key1.Value == "KEY-12345", "IdempotencyKey.Value preserves key string");
Assert(key1 == key2, "IdempotencyKey equality holds for same value");
Assert(key1 != key3, "IdempotencyKey inequality holds for different value");
Assert(key1.CompareTo(key3) < 0, "IdempotencyKey ordinal comparison works");

bool emptyKeyThrows = false;
try
{
    _ = new IdempotencyKey("   ");
}
catch (ArgumentException)
{
    emptyKeyThrows = true;
}
Assert(emptyKeyThrows, "IdempotencyKey throws on whitespace or empty value");

bool tooLongKeyThrows = false;
try
{
    _ = new IdempotencyKey(new string('X', 129));
}
catch (ArgumentOutOfRangeException)
{
    tooLongKeyThrows = true;
}
Assert(tooLongKeyThrows, "IdempotencyKey throws when exceeding 128 characters");

// ── 2. IdempotencyScope Tests ────────────────────────────────────────────────
Console.WriteLine("\n--- 2. IdempotencyScope Invariants ---");

var scope1 = new IdempotencyScope("payments");
var scope2 = IdempotencyScope.Create("payments");
var scope3 = IdempotencyScope.Default;

Assert(scope1.Value == "payments", "IdempotencyScope.Value preserves scope string");
Assert(scope1 == scope2, "IdempotencyScope equality holds for same value");
Assert(scope3.Value == "default", "IdempotencyScope.Default is 'default'");

// ── 3. Fingerprint Hasher Tests ──────────────────────────────────────────────
Console.WriteLine("\n--- 3. IdempotencyFingerprintHasher Determinism ---");

var payload = Encoding.UTF8.GetBytes("{\"amount\":100,\"currency\":\"USD\"}");
var fp1 = IdempotencyFingerprintHasher.Compute("POST", "/payments", "tenant-1", "user-1", payload);
var fp2 = IdempotencyFingerprintHasher.Compute("POST", "/payments", "tenant-1", "user-1", payload);
var fpDifferentPayload = IdempotencyFingerprintHasher.Compute("POST", "/payments", "tenant-1", "user-1", Encoding.UTF8.GetBytes("{\"amount\":500}"));

Assert(fp1 == fp2, "Fingerprint is deterministic for identical inputs");
Assert(fp1 != fpDifferentPayload, "Fingerprint differs when request payload changes");

// ── 4. InMemoryIdempotencyStore Atomic Workflows ─────────────────────────────
Console.WriteLine("\n--- 4. InMemoryIdempotencyStore Workflows ---");

var store = new InMemoryIdempotencyStore();
var tenantId = Guid.NewGuid();
var testKey = new IdempotencyKey("CLAIM-001");

// 4a. Initial Acquire
var claim1 = await store.TryAcquireAsync(
    tenantId, "orders", testKey, fp1, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

Assert(claim1.Status == ClaimResultStatus.AcquiredNew, "Initial claim succeeds as AcquiredNew");
Assert(claim1.OwnerToken.HasValue, "OwnerToken is assigned on acquisition");
Assert(claim1.ConcurrencyVersion == 1, "Initial concurrency version is 1");

// 4b. Concurrent Conflict Attempt
var claim2 = await store.TryAcquireAsync(
    tenantId, "orders", testKey, fp1, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

Assert(claim2.Status == ClaimResultStatus.InFlightConflict, "Concurrent claim returns InFlightConflict");

// 4c. Fingerprint Mismatch Attempt
var claimMismatch = await store.TryAcquireAsync(
    tenantId, "orders", testKey, fpDifferentPayload, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

Assert(claimMismatch.Status == ClaimResultStatus.FingerprintMismatch, "Mismatched payload returns FingerprintMismatch");

// 4d. Mark Completed
var serializedResponse = Encoding.UTF8.GetBytes("{\"orderId\":\"ORD-1\",\"status\":\"Created\"}");
string[] locationHeaderValues = ["/orders/ORD-1"];
var headers = new Dictionary<string, string[]> { ["Location"] = locationHeaderValues };
var markCompleted = await store.MarkCompletedAsync(
    tenantId, "orders", testKey, claim1.OwnerToken!.Value, claim1.ConcurrencyVersion!.Value,
    201, headers, serializedResponse, TimeSpan.FromDays(7));

Assert(markCompleted, "MarkCompletedAsync returns true with valid fencing tokens");

// 4e. Subsequent Replay Claim
var replayClaim = await store.TryAcquireAsync(
    tenantId, "orders", testKey, fp1, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

Assert(replayClaim.Status == ClaimResultStatus.CompletedReplay, "Subsequent claim returns CompletedReplay");
Assert(replayClaim.CachedResponse is not null, "CachedResponse is retrieved");
Assert(replayClaim.CachedResponse!.StatusCode == 201, "CachedResponse status code matches original");

// ── 5. Full IdempotencyEngine Orchestration ──────────────────────────────────
Console.WriteLine("\n--- 5. IdempotencyEngine Orchestration ---");

var options = new IdempotencyOptions();
var policy = new DefaultIdempotencyPolicy(options);
var serializer = new AotTestSerializer();
var accessor = new TestContextAccessor();
var engine = new IdempotencyEngine(store, policy, serializer, accessor, NullLogger<IdempotencyEngine>.Instance);

int businessExecutionCount = 0;
var engineKey = new IdempotencyKey("ENGINE-KEY-1");
var engineFp = IdempotencyFingerprintHasher.Compute("POST", "/test", tenantId.ToString(), null, ReadOnlySpan<byte>.Empty);

Task<OrderModel> ExecuteOrderAsync(CancellationToken ct)
{
    businessExecutionCount++;
    return Task.FromResult(new OrderModel("ORD-AOT", 99.99m));
}

// First invocation -> executes business logic
var result1 = await engine.ExecuteAsync(tenantId, "orders", engineKey, engineFp, ExecuteOrderAsync);
Assert(result1.OrderId == "ORD-AOT", "First execution returns computed result");
Assert(businessExecutionCount == 1, "Business logic executed exactly once on first call");

// Second invocation with same key -> served from cache without executing business logic
var result2 = await engine.ExecuteAsync(tenantId, "orders", engineKey, engineFp, ExecuteOrderAsync);
Assert(result2.OrderId == "ORD-AOT", "Second execution returns replayed result");
Assert(businessExecutionCount == 1, "Business logic was NOT executed on replay (effective-once guaranteed)");

// ── 6. OpenTelemetry Diagnostics Validation ──────────────────────────────────
Console.WriteLine("\n--- 6. Diagnostics Verification ---");
IdempotencyDiagnostics.RecordRequest("orders");
IdempotencyDiagnostics.RecordDuplicate("orders");
IdempotencyDiagnostics.RecordReplayed("orders");
IdempotencyDiagnostics.RecordDuration(12.5, "orders");
Assert(IdempotencyDiagnostics.Meter.Name == "EricksonLopez.Idempotency", "Meter name is EricksonLopez.Idempotency");
Assert(IdempotencyDiagnostics.ActivitySource.Name == "EricksonLopez.Idempotency", "ActivitySource name is EricksonLopez.Idempotency");

Console.WriteLine("\n=================================================");
Console.WriteLine($" ALL {passedTests} NATIVE AOT SUITE TESTS PASSED SUCCESSFULLY! ");
Console.WriteLine("=== AOT Validator: OK ===");
Console.WriteLine("=================================================");
