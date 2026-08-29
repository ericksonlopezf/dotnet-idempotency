// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;
using AwesomeAssertions;
using EricksonLopez.Idempotency;
using Xunit;

namespace EricksonLopez.Idempotency.ArchitectureTests;

/// <summary>
/// Architecture rules verifying purity, value object immutability, and interface contract adherence.
/// </summary>
public sealed class PurityAndImmutabilityTests
{
    /// <summary>
    /// Verifies that IdempotencyKey is a struct value object without public setters.
    /// </summary>
    [Fact]
    public void IdempotencyKey_MustBe_ValueType_And_Immutable()
    {
        var type = typeof(IdempotencyKey);
        type.IsValueType.Should().BeTrue("IdempotencyKey must be a readonly struct value object.");

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            prop.CanWrite.Should().BeFalse($"Property '{prop.Name}' on IdempotencyKey must not have a public setter.");
        }
    }

    /// <summary>
    /// Verifies that IdempotencyScope is a struct value object without public setters.
    /// </summary>
    [Fact]
    public void IdempotencyScope_MustBe_ValueType_And_Immutable()
    {
        var type = typeof(IdempotencyScope);
        type.IsValueType.Should().BeTrue("IdempotencyScope must be a readonly struct value object.");

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            prop.CanWrite.Should().BeFalse($"Property '{prop.Name}' on IdempotencyScope must not have a public setter.");
        }
    }

    /// <summary>
    /// Verifies that IIdempotencyStore is an interface.
    /// </summary>
    [Fact]
    public void IIdempotencyStore_MustBe_Interface()
    {
        typeof(IIdempotencyStore).IsInterface.Should().BeTrue(
            because: "IIdempotencyStore is the core storage SPI interface.");
    }

    /// <summary>
    /// Verifies that CachedIdempotencyResponse is an immutable record class.
    /// </summary>
    [Fact]
    public void CachedIdempotencyResponse_MustBe_ImmutableRecord()
    {
        var type = typeof(CachedIdempotencyResponse);
        type.IsClass.Should().BeTrue("CachedIdempotencyResponse must be an immutable record class.");

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var setMethod = prop.GetSetMethod(nonPublic: false);
            if (setMethod != null)
            {
                var isInitOnly = Array.Exists(
                    setMethod.ReturnParameter.GetRequiredCustomModifiers(),
                    m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

                isInitOnly.Should().BeTrue($"Property '{prop.Name}' must be init-only or get-only.");
            }
        }
    }
}
