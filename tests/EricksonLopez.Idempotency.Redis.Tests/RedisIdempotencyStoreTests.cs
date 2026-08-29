// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Redis;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Idempotency.Redis.Tests;

public sealed class RedisIdempotencyStoreTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisIdempotencyOptions _options = new() { KeyPrefix = "test-prefix:" };

    public RedisIdempotencyStoreTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var act1 = () => new RedisIdempotencyStore(null!, _options);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("redis");

        var act2 = () => new RedisIdempotencyStore(_multiplexer, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task TryAcquireAsync_WhenScriptReturns1_ReturnsAcquiredNew()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-acquire-1");

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(k => k[0].ToString() == $"test-prefix:{tenantId}:test-scope:k-acquire-1"),
            Arg.Is<RedisValue[]>(v => v[0].ToString() == "fp-123" && v[2].ToString() == "60000"))
            .Returns(RedisResult.Create(new RedisValue[] { 1, RedisValue.Null, RedisValue.Null, "fp-123" }));

        var result = await store.TryAcquireAsync(
            tenantId,
            "test-scope",
            key,
            "fp-123",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1));

        result.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        result.IsAcquired.Should().BeTrue();
        result.OwnerToken.Should().NotBeNull();
        result.OwnerToken.Should().NotBe(Guid.Empty);
        result.ConcurrencyVersion.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenScriptReturns2_ReturnsCompletedReplayWithDeserializedPayload()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-replay-2");

        var headers = new Dictionary<string, string[]> { ["X-Header"] = new[] { "V1", "V2" } };
        var body = new byte[] { 1, 2, 3, 4 };
        var payloadJson = JsonSerializer.Serialize(
            new RedisResponsePayload(200, headers, body),
            RedisIdempotencyJsonContext.Default.RedisResponsePayload);

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 2, 200, payloadJson, "fp-replay" }));

        var result = await store.TryAcquireAsync(
            tenantId,
            "test-scope",
            key,
            "fp-replay",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1));

        result.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result.IsReplay.Should().BeTrue();
        result.CachedResponse.Should().NotBeNull();
        result.CachedResponse!.StatusCode.Should().Be(200);
        result.CachedResponse.Headers.Should().ContainKey("X-Header");
        result.CachedResponse.Body.ToArray().Should().BeEquivalentTo(body);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenScriptReturns2WithCorruptJsonOrNulls_ReturnsNullCachedResponse()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-corrupt-json");

        // 1. Invalid JSON
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 2, 200, "invalid-json-string", "fp-corrupt" }));

        var result1 = await store.TryAcquireAsync(tenantId, "test-scope", key, "fp-corrupt", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        result1.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result1.CachedResponse.Should().BeNull();

        // 2. Null JSON
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 2, 200, RedisValue.Null, "fp-null" }));

        var result2 = await store.TryAcquireAsync(tenantId, "test-scope", key, "fp-null", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        result2.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result2.CachedResponse.Should().BeNull();

        // 3. Null Status Code
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 2, RedisValue.Null, "{}", "fp-null-status" }));

        var result3 = await store.TryAcquireAsync(tenantId, "test-scope", key, "fp-null-status", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        result3.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result3.CachedResponse.Should().BeNull();

        // 4. JSON evaluates to null payload
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 2, 200, "null", "fp-null-payload" }));

        var result4 = await store.TryAcquireAsync(tenantId, "test-scope", key, "fp-null-payload", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        result4.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        result4.CachedResponse.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenScriptReturns3_ReturnsInFlightConflict()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-inflight-3");

        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 3, RedisValue.Null, RedisValue.Null, "fp-inflight" }));

        var result = await store.TryAcquireAsync(tenantId, "test-scope", key, "fp-inflight", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        result.Status.Should().Be(ClaimResultStatus.InFlightConflict);
        result.IsAcquired.Should().BeFalse();
        result.IsReplay.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenScriptReturns4_ReturnsFingerprintMismatch()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-mismatch-4");

        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 4, RedisValue.Null, RedisValue.Null, "existing-fp" }));

        var result = await store.TryAcquireAsync(tenantId, "test-scope", key, "new-fp", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        result.Status.Should().Be(ClaimResultStatus.FingerprintMismatch);
        result.ExistingFingerprint.Should().Be("existing-fp");
    }

    [Fact]
    public async Task TryAcquireAsync_WhenScriptReturnsUnexpectedCode_FallsBackToInFlightConflict()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-unexpected");

        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisValue[] { 999, RedisValue.Null, RedisValue.Null, RedisValue.Null }));

        var result = await store.TryAcquireAsync(tenantId, "test-scope", key, "fp", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        result.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task MarkCompletedAsync_ExecutesCompleteScriptAndReturnsSuccessBoolean()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-complete");
        var ownerToken = Guid.NewGuid();

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(k => k[0].ToString() == $"test-prefix:{tenantId}:test-scope:k-complete"),
            Arg.Is<RedisValue[]>(v => v[0].ToString() == ownerToken.ToString() && v[1].ToString() == "200" && v[3].ToString() == "86400000"))
            .Returns(RedisResult.Create(1));

        var success = await store.MarkCompletedAsync(
            tenantId,
            "test-scope",
            key,
            ownerToken,
            concurrencyVersion: 1,
            statusCode: 200,
            headers: new Dictionary<string, string[]>(),
            responseBody: new byte[] { 10, 20 },
            retentionDuration: TimeSpan.FromDays(1));

        success.Should().BeTrue();

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(0));

        var fail = await store.MarkCompletedAsync(
            tenantId,
            "test-scope",
            key,
            ownerToken,
            1,
            200,
            new Dictionary<string, string[]>(),
            ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromDays(1));

        fail.Should().BeFalse();
    }

    [Fact]
    public async Task MarkFailedAsync_ExecutesFailScriptAndReturnsSuccessBoolean()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("k-fail");
        var ownerToken = Guid.NewGuid();

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(k => k[0].ToString() == $"test-prefix:{tenantId}:test-scope:k-fail"),
            Arg.Is<RedisValue[]>(v => v[0].ToString() == ownerToken.ToString()))
            .Returns(RedisResult.Create(1));

        var success = await store.MarkFailedAsync(
            tenantId,
            "test-scope",
            key,
            ownerToken,
            concurrencyVersion: 1);

        success.Should().BeTrue();

        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(0));

        var fail = await store.MarkFailedAsync(tenantId, "test-scope", key, ownerToken, 1);
        fail.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredRecordsAsync_ReturnsZeroWithoutThrowing()
    {
        var store = new RedisIdempotencyStore(_multiplexer, _options);
        var count = await store.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow, 100);
        count.Should().Be(0);
    }

    [Fact]
    public void RedisResponsePayload_GettersAndSetters_Roundtrip()
    {
        var defaultPayload = new RedisResponsePayload();
        defaultPayload.StatusCode.Should().Be(0);
        defaultPayload.Headers.Should().BeEmpty();
        defaultPayload.Body.Should().BeEmpty();

        var payload1 = new RedisResponsePayload();
        payload1.StatusCode = 201;
        payload1.Headers = new Dictionary<string, string[]> { ["X-Test"] = new[] { "Val" } };
        payload1.Body = new byte[] { 99, 100 };

        payload1.StatusCode.Should().Be(201);
        payload1.Headers.Should().ContainKey("X-Test");
        payload1.Body.Should().BeEquivalentTo(new byte[] { 99, 100 });

        var payload2 = new RedisResponsePayload(404, new Dictionary<string, string[]>(), new byte[] { 1 });
        payload2.StatusCode.Should().Be(404);
        payload2.Body.Should().BeEquivalentTo(new byte[] { 1 });
    }
}
