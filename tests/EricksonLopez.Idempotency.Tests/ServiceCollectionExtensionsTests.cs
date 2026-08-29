// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EricksonLopez.Idempotency.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddIdempotencyCore_DefaultConfiguration_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        services.AddIdempotencyCore();

        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IdempotencyOptions>().Should().NotBeNull();
        sp.GetRequiredService<IIdempotencyPolicy>().Should().BeOfType<DefaultIdempotencyPolicy>();
        sp.GetRequiredService<IIdempotencyFingerprintGenerator>().Should().BeOfType<IdempotencyFingerprintHasher>();
        sp.GetRequiredService<IIdempotencySerializer>().Should().BeOfType<SystemTextJsonIdempotencySerializer>();
        sp.GetRequiredService<IIdempotencyContextAccessor>().Should().BeOfType<AsyncLocalIdempotencyContextAccessor>();

        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IdempotencyEngine>().Should().NotBeNull();
    }

    [Fact]
    public void AddIdempotencyCore_WithCustomConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        services.AddIdempotencyCore(options =>
        {
            options.HeaderName = "X-Custom-Idempotency";
            options.RequireIdempotencyKey = true;
            options.DefaultLeaseDuration = TimeSpan.FromMinutes(2);
        });

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IdempotencyOptions>();

        options.HeaderName.Should().Be("X-Custom-Idempotency");
        options.RequireIdempotencyKey.Should().BeTrue();
        options.DefaultLeaseDuration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AddIdempotencyCleanupService_DefaultAndCustom_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        services.AddIdempotencyCleanupService(cleanup =>
        {
            cleanup.Interval = TimeSpan.FromMinutes(30);
            cleanup.BatchSize = 500;
        });

        var sp = services.BuildServiceProvider();

        var cleanupOptions = sp.GetRequiredService<IdempotencyCleanupOptions>();
        cleanupOptions.Interval.Should().Be(TimeSpan.FromMinutes(30));
        cleanupOptions.BatchSize.Should().Be(500);

        var hostedServices = sp.GetServices<IHostedService>();
        hostedServices.Should().Contain(s => s.GetType().Name == "IdempotencyCleanupBackgroundService");

        var actNull = () => ServiceCollectionExtensions.AddIdempotencyCleanupService(null!);
        actNull.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddIdempotencyCleanupService_WithoutConfigure_UsesDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIdempotencyCleanupService();

        var sp = services.BuildServiceProvider();
        var cleanupOptions = sp.GetRequiredService<IdempotencyCleanupOptions>();
        cleanupOptions.Interval.Should().Be(TimeSpan.FromHours(1));
        cleanupOptions.BatchSize.Should().Be(1000);
    }
}
