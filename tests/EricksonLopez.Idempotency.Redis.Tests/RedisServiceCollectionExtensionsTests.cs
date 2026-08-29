// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Redis;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Idempotency.Redis.Tests;

public sealed class RedisServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRedisIdempotency_NullServices_ThrowsArgumentNullException()
    {
        var act1 = () => RedisServiceCollectionExtensions.AddRedisIdempotency(null!, configure: null);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("services");

        var act2 = () => RedisServiceCollectionExtensions.AddRedisIdempotency(null!, "localhost:6379", configure: null);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddRedisIdempotency_NullConnectionString_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddRedisIdempotency(connectionString: null!, configure: null);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionString");
    }

    [Fact]
    public void AddRedisIdempotency_WithExistingMultiplexer_RegistersOptionsAndStore()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        var result = services.AddRedisIdempotency(options =>
        {
            options.KeyPrefix = "custom-prefix:";
        });

        result.Should().BeSameAs(services);

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<RedisIdempotencyOptions>();
        var store = provider.GetService<IIdempotencyStore>();

        options.Should().NotBeNull();
        options!.KeyPrefix.Should().Be("custom-prefix:");
        store.Should().NotBeNull();
        store.Should().BeOfType<RedisIdempotencyStore>();
    }

    [Fact]
    public void AddRedisIdempotency_WithDefaultOptions_RegistersDefaultPrefix()
    {
        var services = new ServiceCollection();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        services.AddRedisIdempotency();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<RedisIdempotencyOptions>();

        options.KeyPrefix.Should().Be("idempotency:");
    }

    [Fact]
    public void AddRedisIdempotency_WithConnectionString_RegistersServices()
    {
        var services = new ServiceCollection();
        var result = services.AddRedisIdempotency("localhost:6379", opt => opt.KeyPrefix = "custom:");

        result.Should().BeSameAs(services);
        services.Should().Contain(sd => sd.ServiceType == typeof(IConnectionMultiplexer));
        services.Should().Contain(sd => sd.ServiceType == typeof(IIdempotencyStore) && sd.ImplementationType == typeof(RedisIdempotencyStore));
    }
}
