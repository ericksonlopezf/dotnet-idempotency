// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Idempotency.Abstractions.Tests;

public sealed class IdempotencyKeyTests
{
    [Fact]
    public void Constructor_WithValidValue_SetsProperty()
    {
        var key = new IdempotencyKey("test-key-123");
        key.Value.Should().Be("test-key-123");
        key.ToString().Should().Be("test-key-123");
    }

    [Fact]
    public void Create_WithValidValue_ReturnsEquivalentInstance()
    {
        var key = IdempotencyKey.Create("custom-key-xyz");
        key.Value.Should().Be("custom-key-xyz");
        key.ToString().Should().Be("custom-key-xyz");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Constructor_WithNullOrWhitespace_ThrowsArgumentException(string? invalidValue)
    {
        var act = () => new IdempotencyKey(invalidValue!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespace_ThrowsArgumentException(string? invalidValue)
    {
        var act = () => IdempotencyKey.Create(invalidValue!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithExactMaxLength128_Succeeds()
    {
        var exactString = new string('k', 128);
        var key = new IdempotencyKey(exactString);
        key.Value.Should().Be(exactString);
        key.Value.Length.Should().Be(128);
    }

    [Fact]
    public void Constructor_WithTooLongValue_ThrowsArgumentOutOfRangeException()
    {
        var longString = new string('a', 129);
        var act = () => new IdempotencyKey(longString);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*Idempotency key cannot exceed 128 characters*");
    }

    [Fact]
    public void Create_WithTooLongValue_ThrowsArgumentOutOfRangeException()
    {
        var longString = new string('a', 129);
        var act = () => IdempotencyKey.Create(longString);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*Idempotency key cannot exceed 128 characters*");
    }

    [Fact]
    public void Conversions_ImplicitToString_And_ExplicitFromString_WorkCorrectly()
    {
        var key = new IdempotencyKey("convert-me");
        string stringValue = key;
        stringValue.Should().Be("convert-me");

        var explicitKey = (IdempotencyKey)"convert-me";
        explicitKey.Value.Should().Be("convert-me");
        explicitKey.Should().Be(key);
    }

    [Fact]
    public void ExplicitConversion_WithInvalidString_ThrowsException()
    {
        var actNull = () => (IdempotencyKey)(string)null!;
        actNull.Should().Throw<ArgumentException>();

        var actTooLong = () => (IdempotencyKey)new string('z', 130);
        actTooLong.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EqualityAndHashCode_WorkDeterministically()
    {
        var key1 = new IdempotencyKey("same-key");
        var key2 = new IdempotencyKey("same-key");
        var key3 = new IdempotencyKey("different-key");

        (key1 == key2).Should().BeTrue();
        (key1 != key2).Should().BeFalse();
        (key1 == key3).Should().BeFalse();
        (key1 != key3).Should().BeTrue();

        key1.Equals(key2).Should().BeTrue();
        key1.Equals((object)key2).Should().BeTrue();
        key1.Equals(key3).Should().BeFalse();
        key1.Equals((object)key3).Should().BeFalse();
        key1.Equals(null).Should().BeFalse();
        key1.Equals("not-a-key").Should().BeFalse();

        key1.GetHashCode().Should().Be(key2.GetHashCode());
    }

    [Fact]
    public void ComparisonOperators_WorkDeterministically()
    {
        var keyA = new IdempotencyKey("aaa");
        var keyB = new IdempotencyKey("bbb");
        var keyA2 = new IdempotencyKey("aaa");

        (keyA < keyB).Should().BeTrue();
        (keyA <= keyB).Should().BeTrue();
        (keyB > keyA).Should().BeTrue();
        (keyB >= keyA).Should().BeTrue();

        (keyA <= keyA2).Should().BeTrue();
        (keyA >= keyA2).Should().BeTrue();
        (keyA < keyA2).Should().BeFalse();
        (keyA > keyA2).Should().BeFalse();

        keyA.CompareTo(keyB).Should().BeNegative();
        keyB.CompareTo(keyA).Should().BePositive();
        keyA.CompareTo(keyA2).Should().Be(0);
    }

    [Fact]
    public void CompareToObject_WorksCorrectly()
    {
        var keyA = new IdempotencyKey("aaa");
        var keyB = new IdempotencyKey("bbb");

        keyA.CompareTo((object)keyB).Should().BeNegative();
        keyB.CompareTo((object)keyA).Should().BePositive();
        keyA.CompareTo((object)new IdempotencyKey("aaa")).Should().Be(0);
        keyA.CompareTo(null).Should().Be(1);

        var act = () => keyA.CompareTo("some-string");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Object must be of type IdempotencyKey*");
    }
}
