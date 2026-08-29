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
    public void Empty_And_DefaultStruct_BehaveCorrectly()
    {
        IdempotencyKey.Empty.Value.Should().Be(string.Empty);
        IdempotencyKey.Empty.IsEmpty.Should().BeTrue();
        IdempotencyKey.Empty.ToString().Should().Be(string.Empty);

        IdempotencyKey defaultKey = default;
        defaultKey.Value.Should().Be(string.Empty);
        defaultKey.IsEmpty.Should().BeTrue();
        defaultKey.ToString().Should().Be(string.Empty);

        var initializedKey = new IdempotencyKey("active-key");
        initializedKey.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Constructor_TrimsSurroundingWhitespace()
    {
        var key = new IdempotencyKey("   padded-key   ");
        key.Value.Should().Be("padded-key");

        var paddedMax = "   " + new string('k', 128) + "   ";
        var keyMax = new IdempotencyKey(paddedMax);
        keyMax.Value.Length.Should().Be(128);
    }

    [Fact]
    public void Create_WithGuid_WorksAndValidatesInvariants()
    {
        var actEmpty = () => IdempotencyKey.Create(Guid.Empty);
        actEmpty.Should().Throw<ArgumentException>()
            .WithParameterName("identifier")
            .WithMessage("*Idempotency key cannot be created from Guid.Empty.*");

        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var key = IdempotencyKey.Create(id);
        key.Value.Should().Be("0123456789abcdef0123456789abcdef");
    }

    [Fact]
    public void NewKey_GeneratesNonEmptyUniqueKey()
    {
        var key1 = IdempotencyKey.NewKey();
        var key2 = IdempotencyKey.NewKey();

        key1.IsEmpty.Should().BeFalse();
        key1.Value.Length.Should().Be(32);
        (key1 != key2).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void TryParse_WithInvalidNullOrWhitespace_ReturnsFalse(string? candidate)
    {
        var success = IdempotencyKey.TryParse(candidate, out var key);
        success.Should().BeFalse();
        key.Should().Be(default(IdempotencyKey));
        key.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TryParse_WithTooLongString_ReturnsFalse()
    {
        var tooLong = new string('z', 129);
        var success = IdempotencyKey.TryParse(tooLong, out var key);
        success.Should().BeFalse();
        key.Should().Be(default(IdempotencyKey));
    }

    [Fact]
    public void TryParse_WithValidAndBoundaryStrings_ReturnsTrue()
    {
        var exactMax = new string('z', 128);
        var successMax = IdempotencyKey.TryParse(exactMax, out var keyMax);
        successMax.Should().BeTrue();
        keyMax.Value.Should().Be(exactMax);
        keyMax.IsEmpty.Should().BeFalse();

        var paddedString = "   valid-parsed-key   ";
        var successPadded = IdempotencyKey.TryParse(paddedString, out var keyPadded);
        successPadded.Should().BeTrue();
        keyPadded.Value.Should().Be("valid-parsed-key");
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
