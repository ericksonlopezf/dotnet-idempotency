// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace EricksonLopez.Idempotency.AspNetCore.Tests;

public sealed class IdempotentEndpointFilterTests
{
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly IdempotencyOptions _options = new();

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var act1 = () => new IdempotentEndpointFilter(null!, _options);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("store");

        var act2 = () => new IdempotentEndpointFilter(_store, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task InvokeAsync_WhenMissingHeaderAndRequired_ReturnsProblem400WithDetails()
    {
        var options = new IdempotencyOptions { RequireIdempotencyKey = true, HeaderName = "X-Idempotency-Key" };
        var filter = new IdempotentEndpointFilter(_store, options);

        var context = CreateEndpointContext(key: null, body: "");
        var executed = false;

        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("OK");
        });

        executed.Should().BeFalse();
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result!;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Title.Should().Be("Missing Idempotency Key");
        problem.ProblemDetails.Detail.Should().Be("The 'X-Idempotency-Key' request header is mandatory for this endpoint.");
    }

    [Fact]
    public async Task InvokeAsync_WhenWhitespaceHeaderAndRequired_ReturnsProblem400()
    {
        var options = new IdempotencyOptions { RequireIdempotencyKey = true };
        var filter = new IdempotentEndpointFilter(_store, options);

        var context = CreateEndpointContext(key: "   ", body: "");
        var executed = false;

        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("OK");
        });

        executed.Should().BeFalse();
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result!;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_WhenWhitespaceHeaderAndNotRequired_CallsNext()
    {
        var options = new IdempotencyOptions { RequireIdempotencyKey = false };
        var filter = new IdempotentEndpointFilter(_store, options);

        var context = CreateEndpointContext(key: "   ", body: "");
        var executed = false;

        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("ResultData");
        });

        executed.Should().BeTrue();
        result.Should().Be("ResultData");
    }

    [Fact]
    public async Task InvokeAsync_WhenMissingHeaderAndNotRequired_CallsNext()
    {
        var options = new IdempotencyOptions { RequireIdempotencyKey = false };
        var filter = new IdempotentEndpointFilter(_store, options);

        var context = CreateEndpointContext(key: null, body: "");
        var executed = false;

        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("ResultData");
        });

        executed.Should().BeTrue();
        result.Should().Be("ResultData");
    }

    [Fact]
    public async Task InvokeAsync_WithNonSeekableStream_EnablesRequestBufferingSoBodyCanBeRead()
    {
        var filter = new IdempotentEndpointFilter(_store, _options);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Idempotency-Key"] = "k-filter-nonseek";
        httpContext.Request.Path = "/api/v1/items";
        httpContext.Request.Method = "POST";

        var bytes = Encoding.UTF8.GetBytes("{\"buffer\":true}");
        httpContext.Request.Body = new NonSeekableReadStream(new MemoryStream(bytes));
        httpContext.Response.Body = new MemoryStream();

        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        string? downstreamBody = null;

        await filter.InvokeAsync(context, async ctx =>
        {
            using var reader = new StreamReader(ctx.HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
            ctx.HttpContext.Response.StatusCode = 200;
            return "OK";
        });

        downstreamBody.Should().Be("{\"buffer\":true}");
    }

    [Fact]
    public async Task InvokeAsync_WithoutAttribute_ExtractsRequestPathAsScope()
    {
        var recordingStore = new RecordingMockStore();
        var filter = new IdempotentEndpointFilter(recordingStore, _options);

        var context = CreateEndpointContext("k-filter-scope", "{}");
        context.HttpContext.Request.Path = "/api/v1/items";

        await filter.InvokeAsync(context, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("OK");
        });

        recordingStore.LastScope.Should().Be("/api/v1/items");
    }

    [Fact]
    public async Task InvokeAsync_WhenUserHasSubjectClaim_IncludesSubjectInFingerprint()
    {
        var filter = new IdempotentEndpointFilter(_store, _options);

        var ctxUser1 = CreateEndpointContext("k-filter-sub", "{\"val\":1}");
        ctxUser1.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-alpha") }));
        await filter.InvokeAsync(ctxUser1, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("OK");
        });

        var ctxUser2 = CreateEndpointContext("k-filter-sub", "{\"val\":1}");
        ctxUser2.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-beta") }));
        var result = await filter.InvokeAsync(ctxUser2, ctx => ValueTask.FromResult<object?>("OK"));

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result!;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task InvokeAsync_WhenFingerprintMismatch_ReturnsProblem409WithDetails()
    {
        var filter = new IdempotentEndpointFilter(_store, _options);

        var ctx1 = CreateEndpointContext("k-filter-tamper", "{\"item\":1}");
        await filter.InvokeAsync(ctx1, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("OK");
        });

        var ctx2 = CreateEndpointContext("k-filter-tamper", "{\"item\":2}");
        var result = await filter.InvokeAsync(ctx2, ctx => ValueTask.FromResult<object?>("OK"));

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result!;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Title.Should().Be("Idempotency Key Conflict");
        problem.ProblemDetails.Detail.Should().Be("Idempotency key mismatch: a previous request used the same key with different payload parameters.");
    }

    [Fact]
    public async Task InvokeAsync_WhenInFlightConflict_ReturnsProblem409WithDetailsAndRetryAfter()
    {
        var mockStore = new InFlightConflictMockStore();
        var filter = new IdempotentEndpointFilter(mockStore, _options);

        var context = CreateEndpointContext("k-filter-inflight", "{\"data\":true}");
        var result = await filter.InvokeAsync(context, ctx => ValueTask.FromResult<object?>("OK"));

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result!;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Title.Should().Be("Operation In Flight");
        problem.ProblemDetails.Detail.Should().Be("A concurrent request with the same idempotency key is currently processing.");
        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be("2");
    }

    [Fact]
    public async Task InvokeAsync_WhenCompletedReplay_ReplaysCachedBodyAndReturnsEmptyResult()
    {
        var filter = new IdempotentEndpointFilter(_store, _options);

        var ctx1 = CreateEndpointContext("k-filter-replay", "{\"orderId\":99}");
        ctx1.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "sub-456") }));

        await filter.InvokeAsync(ctx1, async ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 201;
            ctx.HttpContext.Response.Headers["X-Custom"] = "FilterValue";
            ctx.HttpContext.Response.Headers["X-Null-Header"] = new StringValues(new string[] { null! });
            ctx.HttpContext.Response.Headers[":status"] = "201";
            ctx.HttpContext.Response.Headers["Transfer-Encoding"] = "chunked";
            await ctx.HttpContext.Response.WriteAsync("{\"created\":true}");
            return "OriginalResult";
        });

        var ctx2 = CreateEndpointContext("k-filter-replay", "{\"orderId\":99}");
        ctx2.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "sub-456") }));

        var replayResult = await filter.InvokeAsync(ctx2, ctx => ValueTask.FromResult<object?>("ShouldNotExecute"));

        replayResult.Should().BeOfType<EmptyHttpResult>();
        ctx2.HttpContext.Response.StatusCode.Should().Be(201);
        ctx2.HttpContext.Response.Headers["X-Idempotency-Replayed"].ToString().Should().Be("true");
        ctx2.HttpContext.Response.Headers["X-Custom"].ToString().Should().Be("FilterValue");
        ctx2.HttpContext.Response.Headers["X-Null-Header"].ToString().Should().Be(string.Empty);
        ctx2.HttpContext.Response.Headers.Should().NotContainKey(":status");
        ctx2.HttpContext.Response.Headers.Should().NotContainKey("Transfer-Encoding");

        ctx2.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx2.HttpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        body.Should().Be("{\"created\":true}");
    }

    [Fact]
    public async Task InvokeAsync_WhenCompletedReplayWithNullCachedResponse_DoesNotThrowNullReference()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, null, "fp"));
        var filter = new IdempotentEndpointFilter(mockStore, _options);

        var context = CreateEndpointContext("k-filter-replay-null-cached", "{}");
        var result = await filter.InvokeAsync(context, ctx => ValueTask.FromResult<object?>("OK"));

        result.Should().BeOfType<EmptyHttpResult>();
    }

    [Fact]
    public async Task InvokeAsync_WhenAcquiredNewWithNonNullCachedResponse_ExecutesDownstreamAndDoesNotReplay()
    {
        var cached = new CachedIdempotencyResponse(200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty);
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, cached, "fp"));
        var filter = new IdempotentEndpointFilter(mockStore, _options);

        var context = CreateEndpointContext("k-filter-acquired-with-cached", "{}");
        var executed = false;

        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("DownstreamExecuted");
        });

        executed.Should().BeTrue();
        result.Should().Be("DownstreamExecuted");
        context.HttpContext.Response.Headers.Should().NotContainKey("X-Idempotency-Replayed");
    }

    [Fact]
    public async Task InvokeAsync_WhenNon2xxAndClaimHasNullOwnerToken_DoesNotThrowInvalidOperation()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, 1, null, null));
        var options = new IdempotencyOptions { CacheOnlySuccessResponses = true };
        var filter = new IdempotentEndpointFilter(mockStore, options);

        var context = CreateEndpointContext("k-filter-null-owner-token-fail", "{}");
        var result = await filter.InvokeAsync(context, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 400;
            return ValueTask.FromResult<object?>("Bad");
        });

        result.Should().Be("Bad");
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionAndClaimHasNullOwnerToken_DoesNotThrowInvalidOperation()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, 1, null, null));
        var filter = new IdempotentEndpointFilter(mockStore, _options);

        var context = CreateEndpointContext("k-filter-null-owner-token-ex", "{}");
        var act = () => filter.InvokeAsync(context, ctx => throw new InvalidOperationException("Fail")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Fail");
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    public async Task InvokeAsync_WhenCacheOnlySuccessResponses_PersistsOnly2xx(int statusCode, bool shouldBeCached)
    {
        var options = new IdempotencyOptions { CacheOnlySuccessResponses = true };
        var filter = new IdempotentEndpointFilter(_store, options);
        var executionCount = 0;

        var key = $"k-filter-status-{statusCode}";
        var ctx1 = CreateEndpointContext(key, "{}");

        await filter.InvokeAsync(ctx1, ctx =>
        {
            executionCount++;
            ctx.HttpContext.Response.StatusCode = statusCode;
            return ValueTask.FromResult<object?>($"Res{statusCode}");
        });

        var ctx2 = CreateEndpointContext(key, "{}");
        var result2 = await filter.InvokeAsync(ctx2, ctx =>
        {
            executionCount++;
            ctx.HttpContext.Response.StatusCode = statusCode;
            return ValueTask.FromResult<object?>($"Res{statusCode}");
        });

        if (shouldBeCached)
        {
            executionCount.Should().Be(1);
            result2.Should().BeOfType<EmptyHttpResult>();
            ctx2.HttpContext.Response.Headers.Should().ContainKey("X-Idempotency-Replayed");
        }
        else
        {
            executionCount.Should().Be(2);
            result2.Should().NotBeOfType<EmptyHttpResult>();
            ctx2.HttpContext.Response.Headers.Should().NotContainKey("X-Idempotency-Replayed");
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsException_MarksFailedAndRethrowsAndRestoresBody()
    {
        var filter = new IdempotentEndpointFilter(_store, _options);
        var context = CreateEndpointContext("k-filter-ex", "{\"data\":1}");
        var originalBody = context.HttpContext.Response.Body;

        var act = () => filter.InvokeAsync(context, ctx => throw new InvalidOperationException("Endpoint failure")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Endpoint failure");

        // Verify finally restored response body
        context.HttpContext.Response.Body.Should().BeSameAs(originalBody);

        // Retrying should be allowed because it was marked failed
        var retryContext = CreateEndpointContext("k-filter-ex", "{\"data\":1}");
        var result = await filter.InvokeAsync(retryContext, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("Recovered");
        });

        result.Should().Be("Recovered");
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestPathIsEmpty_FallsBackToSlashScope()
    {
        var recordingStore = new RecordingMockStore();
        var filter = new IdempotentEndpointFilter(recordingStore, _options);
        var context = CreateEndpointContext("k-filter-empty-path", "{}");
        context.HttpContext.Request.Path = default;

        var result = await filter.InvokeAsync(context, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("EmptyPathOK");
        });

        result.Should().Be("EmptyPathOK");
        recordingStore.LastScope.Should().Be("/");
    }

    private static EndpointFilterInvocationContext CreateEndpointContext(string? key, string body)
    {
        var httpContext = new DefaultHttpContext();
        if (key != null)
        {
            httpContext.Request.Headers["Idempotency-Key"] = key;
        }

        httpContext.Request.Path = "/api/v1/items";
        httpContext.Request.Method = "POST";

        var bytes = Encoding.UTF8.GetBytes(body);
        httpContext.Request.Body = new MemoryStream(bytes);
        httpContext.Response.Body = new MemoryStream();

        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    private sealed class DefaultEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public DefaultEndpointFilterInvocationContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments { get; } = new List<object?>();
        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;
        public NonSeekableReadStream(Stream inner) => _inner = inner;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingMockStore : IIdempotencyStore
    {
        public string? LastScope { get; private set; }

        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            LastScope = scope;
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
