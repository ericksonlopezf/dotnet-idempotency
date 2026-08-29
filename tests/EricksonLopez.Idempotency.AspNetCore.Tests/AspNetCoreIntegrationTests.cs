// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Idempotency.AspNetCore.Tests;

public sealed class AspNetCoreIntegrationTests
{
    [Fact]
    public async Task IdempotencyMiddleware_WithValidKey_ExecutesAndReplays()
    {
        var store = new InMemoryIdempotencyStore();
        var options = new IdempotencyOptions { RequireIdempotencyKey = false };

        var executionCount = 0;
        RequestDelegate next = ctx =>
        {
            executionCount++;
            ctx.Response.StatusCode = 201;
            return ctx.Response.WriteAsync("{\"status\":\"created\"}");
        };

        var middleware = new IdempotencyMiddleware(next);

        // First execution
        var context1 = CreateHttpContext("k-aspnet-1", "{\"item\":\"laptop\"}");
        await middleware.InvokeAsync(context1, store, options);

        context1.Response.StatusCode.Should().Be(201);
        executionCount.Should().Be(1);

        // Second execution (replay)
        var context2 = CreateHttpContext("k-aspnet-1", "{\"item\":\"laptop\"}");
        await middleware.InvokeAsync(context2, store, options);

        context2.Response.StatusCode.Should().Be(201);
        context2.Response.Headers.Should().ContainKey("X-Idempotency-Replayed");
        executionCount.Should().Be(1);
    }

    [Fact]
    public async Task IdempotencyMiddleware_WithMismatchedPayload_ReturnsConflict409()
    {
        var store = new InMemoryIdempotencyStore();
        var options = new IdempotencyOptions { RequireIdempotencyKey = false };

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var middleware = new IdempotencyMiddleware(next);

        var context1 = CreateHttpContext("k-tamper", "{\"item\":\"laptop\"}");
        await middleware.InvokeAsync(context1, store, options);

        var context2 = CreateHttpContext("k-tamper", "{\"item\":\"phone\"}");
        await middleware.InvokeAsync(context2, store, options);

        context2.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public void AddAspNetCoreIdempotency_RegistersServicesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddAspNetCoreIdempotency();

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IIdempotencyStore>();
        var options = provider.GetService<IdempotencyOptions>();

        options.Should().NotBeNull();
    }

    private static DefaultHttpContext CreateHttpContext(string key, string bodyContent)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.Path = "/api/v1/orders";
        context.Request.Method = "POST";

        var bodyBytes = Encoding.UTF8.GetBytes(bodyContent);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Response.Body = new MemoryStream();

        return context;
    }
}
