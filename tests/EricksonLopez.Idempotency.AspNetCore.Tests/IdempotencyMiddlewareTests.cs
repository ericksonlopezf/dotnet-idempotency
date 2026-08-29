// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace EricksonLopez.Idempotency.AspNetCore.Tests;

public sealed class IdempotencyMiddlewareTests
{
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly IdempotencyOptions _options = new();

    [Fact]
    public void Constructor_NullNext_ThrowsArgumentNullException()
    {
        var act = () => new IdempotencyMiddleware(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("next");
    }

    [Fact]
    public void ExtractTenantId_EvaluatesAllResolutionStrategies()
    {
        var options = new IdempotencyOptions();

        // 1. Custom extractor
        var customTenant = Guid.NewGuid();
        options.TenantIdExtractor = _ => customTenant;
        var ctx1 = new DefaultHttpContext();
        IdempotencyMiddleware.ExtractTenantId(ctx1, options).Should().Be(customTenant);

        // 2. HttpContext.Items["TenantId"]
        options.TenantIdExtractor = null;
        var itemTenant = Guid.NewGuid();
        var ctx2 = new DefaultHttpContext();
        ctx2.Items["TenantId"] = itemTenant;
        IdempotencyMiddleware.ExtractTenantId(ctx2, options).Should().Be(itemTenant);

        // 2b. Items["TenantId"] with non-guid item
        var ctx2b = new DefaultHttpContext();
        ctx2b.Items["TenantId"] = "invalid-guid-item";
        IdempotencyMiddleware.ExtractTenantId(ctx2b, options).Should().Be(Guid.Empty);

        // 3. User JWT Claim "tenant_id"
        var claimTenant = Guid.NewGuid();
        var ctx3 = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim("tenant_id", claimTenant.ToString()) });
        ctx3.User = new ClaimsPrincipal(identity);
        IdempotencyMiddleware.ExtractTenantId(ctx3, options).Should().Be(claimTenant);

        // 3b. User JWT Claim "tenant_id" invalid guid string
        var ctx3b = new DefaultHttpContext();
        var invalidIdentity = new ClaimsIdentity(new[] { new Claim("tenant_id", "not-a-guid") });
        ctx3b.User = new ClaimsPrincipal(invalidIdentity);
        IdempotencyMiddleware.ExtractTenantId(ctx3b, options).Should().Be(Guid.Empty);

        // 4. Default fallback: Guid.Empty
        var ctx4 = new DefaultHttpContext();
        IdempotencyMiddleware.ExtractTenantId(ctx4, options).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task InvokeAsync_WhenNoAttributeAndNotRequiredAndNoHeader_PassesThroughExactlyOnce()
    {
        var executionCount = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executionCount++;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = false };
        var context = CreateContext(key: null, body: "");

        await middleware.InvokeAsync(context, _store, options);

        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WhenWhitespaceHeaderAndNotRequired_PassesThroughExactlyOnce()
    {
        var executionCount = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executionCount++;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = false };
        var context = CreateContext(key: "   ", body: "");

        await middleware.InvokeAsync(context, _store, options);

        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WhenWhitespaceHeaderAndRequired_Returns400BadRequest()
    {
        var executed = false;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = true };
        var context = CreateContext(key: "   ", body: "");

        await middleware.InvokeAsync(context, _store, options);

        executed.Should().BeFalse();
        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_WhenEndpointAttributeEnabledIsFalse_PassesThroughExactlyOnce()
    {
        var executionCount = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executionCount++;
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = true };
        var context = CreateContext(key: null, body: "");
        SetEndpointMetadata(context, new IdempotentAttribute { Enabled = false });

        await middleware.InvokeAsync(context, _store, options);

        executionCount.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_WhenMissingHeaderAndAttributeRequires_Returns400BadRequestWithRFCDetails()
    {
        var executed = false;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = false, HeaderName = "X-Idempotency-Key" };
        var context = CreateContext(key: null, body: "");
        SetEndpointMetadata(context, new IdempotentAttribute { Required = true });

        await middleware.InvokeAsync(context, _store, options);

        executed.Should().BeFalse();
        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Be("application/problem+json");

        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync(context.Response.Body, IdempotencyJsonContext.Default.IdempotencyProblemDetails);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.1");
        problem.Title.Should().Be("Missing Idempotency Key");
        problem.Status.Should().Be(400);
        problem.Detail.Should().Be("The 'X-Idempotency-Key' request header is mandatory for this operation.");
    }

    [Fact]
    public async Task InvokeAsync_WhenMissingHeaderAndOptionsRequires_Returns400BadRequest()
    {
        var executed = false;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = true };
        var context = CreateContext(key: null, body: "");

        await middleware.InvokeAsync(context, _store, options);

        executed.Should().BeFalse();
        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_WhenMissingHeaderAndAttributeExplicitlyNotRequired_PassesThroughExactlyOnce()
    {
        var executionCount = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executionCount++;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions { RequireIdempotencyKey = false };
        var context = CreateContext(key: null, body: "");
        SetEndpointMetadata(context, new IdempotentAttribute { Required = false });

        await middleware.InvokeAsync(context, _store, options);

        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WithNonSeekableStream_EnablesRequestBufferingSoBodyCanBeRead()
    {
        string? downstreamBody = null;
        var middleware = new IdempotencyMiddleware(async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = "k-non-seekable";
        context.Request.Path = "/api/v1/payments";
        context.Request.Method = "POST";

        var bytes = Encoding.UTF8.GetBytes("{\"nonSeekable\":true}");
        context.Request.Body = new NonSeekableReadStream(new MemoryStream(bytes));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _store, _options);

        downstreamBody.Should().Be("{\"nonSeekable\":true}");
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WithCustomAttributeDurations_PassesCustomDurationsToStore()
    {
        var recordingStore = new RecordingMockStore();
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-custom-durations", "{\"item\":1}");
        SetEndpointMetadata(context, new IdempotentAttribute
        {
            Scope = "custom-scope",
            LeaseDurationSeconds = 120,
            RetentionDurationDays = 30
        });

        await middleware.InvokeAsync(context, recordingStore, _options);

        recordingStore.LastScope.Should().Be("custom-scope");
        recordingStore.LastLeaseDuration.Should().Be(TimeSpan.FromSeconds(120));
        recordingStore.LastRetentionDuration.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task InvokeAsync_WithoutAttribute_ExtractsRequestPathAsScope()
    {
        var recordingStore = new RecordingMockStore();
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-path-scope", "{}");
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context, recordingStore, _options);

        recordingStore.LastScope.Should().Be("/api/v1/orders");
    }

    [Fact]
    public async Task InvokeAsync_WithoutAttribute_UsesDefaultOptionsDurations()
    {
        var recordingStore = new RecordingMockStore();
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var options = new IdempotencyOptions
        {
            DefaultLeaseDuration = TimeSpan.FromSeconds(45),
            DefaultRetentionDuration = TimeSpan.FromDays(10)
        };

        var context = CreateContext("k-default-durations", "{\"item\":1}");
        await middleware.InvokeAsync(context, recordingStore, options);

        recordingStore.LastLeaseDuration.Should().Be(TimeSpan.FromSeconds(45));
        recordingStore.LastRetentionDuration.Should().Be(TimeSpan.FromDays(10));
    }

    [Fact]
    public async Task InvokeAsync_WhenUserHasSubjectClaim_IncludesSubjectInFingerprint()
    {
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var ctxUser1 = CreateContext("k-user-subject", "{\"val\":1}");
        ctxUser1.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-alpha") }));
        await middleware.InvokeAsync(ctxUser1, _store, _options);

        var ctxUser2 = CreateContext("k-user-subject", "{\"val\":1}");
        ctxUser2.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-beta") }));
        await middleware.InvokeAsync(ctxUser2, _store, _options);

        ctxUser2.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task InvokeAsync_WhenFingerprintMismatch_Returns409ConflictWithRFCDetails()
    {
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var ctx1 = CreateContext("k-conflict-mismatch", "{\"amount\":100}");
        await middleware.InvokeAsync(ctx1, _store, _options);

        var ctx2 = CreateContext("k-conflict-mismatch", "{\"amount\":200}");
        await middleware.InvokeAsync(ctx2, _store, _options);

        ctx2.Response.StatusCode.Should().Be(409);
        ctx2.Response.ContentType.Should().Be("application/problem+json");

        ctx2.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync(ctx2.Response.Body, IdempotencyJsonContext.Default.IdempotencyProblemDetails);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.10");
        problem.Title.Should().Be("Idempotency Key Conflict");
        problem.Status.Should().Be(409);
        problem.Detail.Should().Be("Idempotency key mismatch: a previous request used the same key with different payload parameters.");
    }

    [Fact]
    public async Task InvokeAsync_WhenInFlightConflict_Returns409ConflictWithRFCDetailsAndDoesNotExecuteNext()
    {
        var mockStore = new InFlightConflictMockStore();
        var executed = false;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-in-flight", "{\"test\":true}");
        await middleware.InvokeAsync(context, mockStore, _options);

        executed.Should().BeFalse();
        context.Response.StatusCode.Should().Be(409);
        context.Response.Headers.RetryAfter.ToString().Should().Be("2");
        context.Response.ContentType.Should().Be("application/problem+json");

        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync(context.Response.Body, IdempotencyJsonContext.Default.IdempotencyProblemDetails);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.10");
        problem.Title.Should().Be("Operation In Flight");
        problem.Status.Should().Be(409);
        problem.Detail.Should().Be("A concurrent request with the same idempotency key is currently processing.");
    }

    [Fact]
    public async Task InvokeAsync_WhenCompletedReplay_ReplaysHeadersAndBody()
    {
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["X-Custom-Header"] = "CustomValue";
            ctx.Response.Headers["X-Null-Header"] = new StringValues(new string[] { null! });
            ctx.Response.Headers[":status"] = "200";
            ctx.Response.Headers["Transfer-Encoding"] = "chunked";
            return ctx.Response.WriteAsync("{\"id\":123}");
        });

        var ctx1 = CreateContext("k-replay-headers", "{\"req\":1}");
        await middleware.InvokeAsync(ctx1, _store, _options);

        var ctx2 = CreateContext("k-replay-headers", "{\"req\":1}");
        await middleware.InvokeAsync(ctx2, _store, _options);

        ctx2.Response.StatusCode.Should().Be(200);
        ctx2.Response.Headers["X-Idempotency-Replayed"].ToString().Should().Be("true");
        ctx2.Response.Headers["X-Custom-Header"].ToString().Should().Be("CustomValue");
        ctx2.Response.Headers["X-Null-Header"].ToString().Should().Be(string.Empty);
        ctx2.Response.Headers.Should().NotContainKey(":status");
        ctx2.Response.Headers.Should().NotContainKey("Transfer-Encoding");

        ctx2.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx2.Response.Body);
        var body = await reader.ReadToEndAsync();
        body.Should().Be("{\"id\":123}");
    }

    [Fact]
    public async Task InvokeAsync_WhenCompletedReplayWithNullCachedResponse_DoesNotThrowNullReference()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, null, "fp"));
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-replay-null-cached", "{}");
        await middleware.InvokeAsync(context, mockStore, _options);

        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenAcquiredNewWithNonNullCachedResponse_ExecutesDownstreamAndDoesNotReplay()
    {
        var cached = new CachedIdempotencyResponse(200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty);
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, cached, "fp"));
        var executionCount = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executionCount++;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-acquired-with-cached", "{}");
        await middleware.InvokeAsync(context, mockStore, _options);

        executionCount.Should().Be(1);
        context.Response.Headers.Should().NotContainKey("X-Idempotency-Replayed");
    }

    [Fact]
    public async Task InvokeAsync_WhenNon2xxAndClaimHasNullOwnerToken_DoesNotThrowInvalidOperation()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, 1, null, null));
        var options = new IdempotencyOptions { CacheOnlySuccessResponses = true };
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 400;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-null-owner-token-fail", "{}");
        await middleware.InvokeAsync(context, mockStore, options);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionAndClaimHasNullOwnerToken_DoesNotThrowInvalidOperation()
    {
        var mockStore = new StaticClaimMockStore(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, null, 1, null, null));
        var middleware = new IdempotencyMiddleware(ctx => throw new InvalidOperationException("Fail"));

        var context = CreateContext("k-null-owner-token-ex", "{}");
        var act = () => middleware.InvokeAsync(context, mockStore, _options);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Fail");
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public async Task InvokeAsync_WhenCacheOnlySuccessResponses_PersistsOnly2xx(int statusCode, bool shouldBeCached)
    {
        var options = new IdempotencyOptions { CacheOnlySuccessResponses = true };
        var executionCount = 0;
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            executionCount++;
            ctx.Response.StatusCode = statusCode;
            return ctx.Response.WriteAsync("{\"code\":" + statusCode + "}");
        });

        var key = $"k-status-cache-{statusCode}";
        var ctx1 = CreateContext(key, "{}");
        await middleware.InvokeAsync(ctx1, _store, options);

        var ctx2 = CreateContext(key, "{}");
        await middleware.InvokeAsync(ctx2, _store, options);

        if (shouldBeCached)
        {
            executionCount.Should().Be(1);
            ctx2.Response.Headers.Should().ContainKey("X-Idempotency-Replayed");
        }
        else
        {
            executionCount.Should().Be(2);
            ctx2.Response.Headers.Should().NotContainKey("X-Idempotency-Replayed");
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsException_MarksFailedAndRethrowsAndRestoresBody()
    {
        var middleware = new IdempotencyMiddleware(ctx => throw new InvalidOperationException("Fatal database error"));

        var context = CreateContext("k-exception", "{\"data\":1}");
        var originalBody = context.Response.Body;
        var act = () => middleware.InvokeAsync(context, _store, _options);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Fatal database error");

        context.Response.Body.Should().BeSameAs(originalBody);

        var nextMiddleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("{\"recovered\":true}");
        });

        var retryContext = CreateContext("k-exception", "{\"data\":1}");
        await nextMiddleware.InvokeAsync(retryContext, _store, _options);

        retryContext.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestPathIsEmpty_FallsBackToSlashScope()
    {
        var recordingStore = new RecordingMockStore();
        var middleware = new IdempotencyMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("k-empty-path", "{}");
        context.Request.Path = default;

        await middleware.InvokeAsync(context, recordingStore, _options);

        context.Response.StatusCode.Should().Be(200);
        recordingStore.LastScope.Should().Be("/");
    }

    private static DefaultHttpContext CreateContext(string? key, string body)
    {
        var context = new DefaultHttpContext();
        if (key != null)
        {
            context.Request.Headers["Idempotency-Key"] = key;
        }

        context.Request.Path = "/api/v1/payments";
        context.Request.Method = "POST";

        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static void SetEndpointMetadata(HttpContext context, object metadata)
    {
        var endpoint = new Endpoint(
            ctx => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "TestEndpoint");

        context.SetEndpoint(endpoint);
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
        public TimeSpan? LastLeaseDuration { get; private set; }
        public TimeSpan? LastRetentionDuration { get; private set; }

        public Task<IdempotencyClaimResult> TryAcquireAsync(Guid tenantId, string scope, IdempotencyKey key, string fingerprint, TimeSpan leaseDuration, TimeSpan retentionDuration, CancellationToken cancellationToken = default)
        {
            LastScope = scope;
            LastLeaseDuration = leaseDuration;
            LastRetentionDuration = retentionDuration;
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
