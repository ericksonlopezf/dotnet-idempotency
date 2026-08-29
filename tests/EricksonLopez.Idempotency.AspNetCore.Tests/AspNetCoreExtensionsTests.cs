// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EricksonLopez.Idempotency.AspNetCore.Tests;

public sealed class AspNetCoreExtensionsTests
{
    [Fact]
    public void UseTenantIdExtractor_NullArguments_ThrowsArgumentNullException()
    {
        var options = new IdempotencyOptions();

        var act1 = () => IdempotencyOptionsAspNetCoreExtensions.UseTenantIdExtractor(null!, ctx => Guid.NewGuid());
        act1.Should().Throw<ArgumentNullException>().WithParameterName("options");

        var act2 = () => options.UseTenantIdExtractor(null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("extractor");
    }

    [Fact]
    public void UseTenantIdExtractor_WhenInvokedWithHttpContext_ReturnsExtractedTenantId()
    {
        var options = new IdempotencyOptions();
        var expectedTenant = Guid.NewGuid();

        options.UseTenantIdExtractor(ctx => expectedTenant);

        var httpContext = new DefaultHttpContext();
        var result = options.TenantIdExtractor!(httpContext);
        result.Should().Be(expectedTenant);
    }

    [Fact]
    public void UseTenantIdExtractor_WhenInvokedWithNonHttpContext_ReturnsGuidEmpty()
    {
        var options = new IdempotencyOptions();
        options.UseTenantIdExtractor(ctx => Guid.NewGuid());

        var resultObj = options.TenantIdExtractor!("non-http-context-string");
        resultObj.Should().Be(Guid.Empty);

        var resultNull = options.TenantIdExtractor!(null!);
        resultNull.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AddAspNetCoreIdempotency_WithoutConfigure_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddAspNetCoreIdempotency();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IdempotencyOptions>();
        options.Should().NotBeNull();
        options.HeaderName.Should().Be("Idempotency-Key");

        var filter = sp.GetRequiredService<IdempotentEndpointFilter>();
        filter.Should().NotBeNull();
    }

    [Fact]
    public void AddAspNetCoreIdempotency_WithConfigure_ExecutesConfigurationAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        var configured = false;

        services.AddAspNetCoreIdempotency(opt =>
        {
            configured = true;
            opt.HeaderName = "X-My-Idempotency";
        });

        configured.Should().BeTrue();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IdempotencyOptions>();
        options.HeaderName.Should().Be("X-My-Idempotency");
    }

    [Fact]
    public void UseIdempotency_NullApp_ThrowsArgumentNullException()
    {
        var act = () => AspNetCoreServiceCollectionExtensions.UseIdempotency(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void UseIdempotency_WithValidApp_AddsMiddleware()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        var sp = services.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);

        app.UseIdempotency();

        var built = app.Build();
        built.Should().NotBeNull();
    }

    [Fact]
    public void WithIdempotency_NullBuilders_ThrowsArgumentNullException()
    {
        var actHandler = () => AspNetCoreServiceCollectionExtensions.WithIdempotency((RouteHandlerBuilder)null!);
        actHandler.Should().Throw<ArgumentNullException>().WithParameterName("builder");

        var actGroup = () => AspNetCoreServiceCollectionExtensions.WithIdempotency((RouteGroupBuilder)null!);
        actGroup.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public async Task WithIdempotency_OnRouteHandlerBuilder_AppliesFilterToRequestDelegate()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddAspNetCoreIdempotency(opt => opt.RequireIdempotencyKey = true);
        var sp = services.BuildServiceProvider();

        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        var routeBuilder = app.MapPost("/test-route", () => "OK");
        var result = routeBuilder.WithIdempotency();
        result.Should().BeSameAs(routeBuilder);

        var dataSource = ((IEndpointRouteBuilder)app).DataSources.First();
        var endpoint = dataSource.Endpoints.OfType<RouteEndpoint>().First(e => e.RoutePattern.RawText == "/test-route");

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Request.Path = "/test-route";
        httpContext.Request.Method = "POST";
        httpContext.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task WithIdempotency_OnRouteGroupBuilder_AppliesFilterToRequestDelegate()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddAspNetCoreIdempotency(opt => opt.RequireIdempotencyKey = true);
        var sp = services.BuildServiceProvider();

        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        var groupBuilder = app.MapGroup("/api");
        var result = groupBuilder.WithIdempotency();
        result.Should().BeSameAs(groupBuilder);
        groupBuilder.MapPost("/grouped-route", () => "OK");

        var dataSource = ((IEndpointRouteBuilder)app).DataSources.First();
        var endpoint = dataSource.Endpoints.OfType<RouteEndpoint>().First(e => e.RoutePattern.RawText == "/api/grouped-route");

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Request.Path = "/api/grouped-route";
        httpContext.Request.Method = "POST";
        httpContext.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
