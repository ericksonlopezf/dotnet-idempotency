// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace EricksonLopez.Idempotency.AspNetCore.Tests;

public sealed class IdempotentEndpointFilterMetadataTests
{
    [Fact]
    public async Task InvokeAsync_WhenEndpointHasCustomScope_UsesCustomScope()
    {
        var recordingStore = new DetailedRecordingMockStore();
        var options = new IdempotencyOptions();
        var filter = new IdempotentEndpointFilter(recordingStore, options);

        var context = CreateContextWithMetadata(
            key: "test-key-1",
            body: "{}",
            new IdempotentAttribute { Scope = "payments.checkout" });

        var result = await filter.InvokeAsync(context, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("OK");
        });

        result.Should().Be("OK");
        recordingStore.LastScope.Should().Be("payments.checkout");
    }

    [Fact]
    public async Task InvokeAsync_WhenEndpointHasCustomLeaseAndRetention_PassesThemToStore()
    {
        var recordingStore = new DetailedRecordingMockStore();
        var options = new IdempotencyOptions
        {
            DefaultLeaseDuration = TimeSpan.FromSeconds(30),
            DefaultRetentionDuration = TimeSpan.FromDays(7)
        };
        var filter = new IdempotentEndpointFilter(recordingStore, options);

        var context = CreateContextWithMetadata(
            key: "test-key-2",
            body: "{}",
            new IdempotentAttribute
            {
                LeaseDurationSeconds = 60,
                RetentionDurationDays = 14
            });

        var result = await filter.InvokeAsync(context, ctx =>
        {
            ctx.HttpContext.Response.StatusCode = 200;
            return ValueTask.FromResult<object?>("OK");
        });

        result.Should().Be("OK");
        recordingStore.LastLeaseDuration.Should().Be(TimeSpan.FromSeconds(60));
        recordingStore.LastRetentionDuration.Should().Be(TimeSpan.FromDays(14));
        recordingStore.LastCompletedRetentionDuration.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public async Task InvokeAsync_WhenAttributeRequiresKey_ReturnsProblem400EvenIfGlobalOptionIsFalse()
    {
        var recordingStore = new DetailedRecordingMockStore();
        var options = new IdempotencyOptions { RequireIdempotencyKey = false };
        var filter = new IdempotentEndpointFilter(recordingStore, options);

        var context = CreateContextWithMetadata(
            key: null,
            body: "{}",
            new IdempotentAttribute { Required = true });

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
    }

    [Fact]
    public async Task InvokeAsync_WhenAttributeSetsRequiredFalse_ProceedsEvenIfGlobalOptionIsTrue()
    {
        var recordingStore = new DetailedRecordingMockStore();
        var options = new IdempotencyOptions { RequireIdempotencyKey = true };
        var filter = new IdempotentEndpointFilter(recordingStore, options);

        var context = CreateContextWithMetadata(
            key: null,
            body: "{}",
            new IdempotentAttribute { Required = false });

        var executed = false;
        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("OptionalSuccess");
        });

        executed.Should().BeTrue();
        result.Should().Be("OptionalSuccess");
    }

    [Fact]
    public async Task InvokeAsync_WhenAttributeDisabled_BypassesIdempotencyCompletely()
    {
        var recordingStore = new DetailedRecordingMockStore();
        var options = new IdempotencyOptions { RequireIdempotencyKey = true };
        var filter = new IdempotentEndpointFilter(recordingStore, options);

        var context = CreateContextWithMetadata(
            key: null,
            body: "{}",
            new IdempotentAttribute { Enabled = false });

        var executed = false;
        var result = await filter.InvokeAsync(context, ctx =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("Bypassed");
        });

        executed.Should().BeTrue();
        result.Should().Be("Bypassed");
        recordingStore.TryAcquireCalled.Should().BeFalse();
    }

    private static EndpointFilterInvocationContext CreateContextWithMetadata(string? key, string body, IdempotentAttribute attribute)
    {
        var httpContext = new DefaultHttpContext();
        if (key != null)
        {
            httpContext.Request.Headers["Idempotency-Key"] = key;
        }

        httpContext.Request.Path = "/api/v1/resource";
        httpContext.Request.Method = "POST";

        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(attribute),
            displayName: "TestMinimalEndpoint");
        httpContext.SetEndpoint(endpoint);

        var bytes = Encoding.UTF8.GetBytes(body);
        httpContext.Request.Body = new MemoryStream(bytes);
        httpContext.Response.Body = new MemoryStream();

        return new TestEndpointFilterInvocationContext(httpContext);
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterInvocationContext(HttpContext httpContext) => HttpContext = httpContext;
        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments { get; } = new List<object?>();
        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }

    private sealed class DetailedRecordingMockStore : IIdempotencyStore
    {
        public bool TryAcquireCalled { get; private set; }
        public string? LastScope { get; private set; }
        public TimeSpan? LastLeaseDuration { get; private set; }
        public TimeSpan? LastRetentionDuration { get; private set; }
        public TimeSpan? LastCompletedRetentionDuration { get; private set; }

        public Task<IdempotencyClaimResult> TryAcquireAsync(
            Guid tenantId,
            string scope,
            IdempotencyKey key,
            string fingerprint,
            TimeSpan leaseDuration,
            TimeSpan retentionDuration,
            CancellationToken cancellationToken = default)
        {
            TryAcquireCalled = true;
            LastScope = scope;
            LastLeaseDuration = leaseDuration;
            LastRetentionDuration = retentionDuration;
            return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, Guid.NewGuid(), 1, null, null));
        }

        public Task<bool> MarkCompletedAsync(
            Guid tenantId,
            string scope,
            IdempotencyKey key,
            Guid ownerToken,
            int concurrencyVersion,
            int statusCode,
            IReadOnlyDictionary<string, string[]> headers,
            ReadOnlyMemory<byte> responseBody,
            TimeSpan retentionDuration,
            CancellationToken cancellationToken = default)
        {
            LastCompletedRetentionDuration = retentionDuration;
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(
            Guid tenantId,
            string scope,
            IdempotencyKey key,
            Guid ownerToken,
            int concurrencyVersion,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> CleanupExpiredRecordsAsync(
            DateTimeOffset utcNow,
            int batchSize,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
