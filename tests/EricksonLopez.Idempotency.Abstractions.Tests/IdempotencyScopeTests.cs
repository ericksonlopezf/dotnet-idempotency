// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Idempotency.Abstractions.Tests;

public sealed class IdempotencyScopeTests
{
    [Fact]
    public void Default_HasExpectedValue()
    {
        IdempotencyScope.Default.Value.Should().Be("default");
        IdempotencyScope.Default.ToString().Should().Be("default");
    }

    [Fact]
    public void Constructor_WithValidValue_SetsProperty()
    {
        var scope = new IdempotencyScope("orders-create");
        scope.Value.Should().Be("orders-create");
        scope.ToString().Should().Be("orders-create");
    }

    [Fact]
    public void Create_WithValidValue_ReturnsEquivalentInstance()
    {
        var scope = IdempotencyScope.Create("payments-process");
        scope.Value.Should().Be("payments-process");
        scope.ToString().Should().Be("payments-process");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Constructor_WithNullOrWhitespace_ThrowsArgumentException(string? invalidValue)
    {
        var act = () => new IdempotencyScope(invalidValue!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespace_ThrowsArgumentException(string? invalidValue)
    {
        var act = () => IdempotencyScope.Create(invalidValue!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithExactMaxLength64_Succeeds()
    {
        var exactString = new string('s', 64);
        var scope = new IdempotencyScope(exactString);
        scope.Value.Should().Be(exactString);
        scope.Value.Length.Should().Be(64);
    }

    [Fact]
    public void Constructor_WithTooLongValue_ThrowsArgumentOutOfRangeException()
    {
        var longString = new string('a', 65);
        var act = () => new IdempotencyScope(longString);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*Idempotency scope cannot exceed 64 characters*");
    }

    [Fact]
    public void Create_WithTooLongValue_ThrowsArgumentOutOfRangeException()
    {
        var longString = new string('a', 65);
        var act = () => IdempotencyScope.Create(longString);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value")
            .WithMessage("*Idempotency scope cannot exceed 64 characters*");
    }

    [Fact]
    public void Conversions_ImplicitToString_And_ExplicitFromString_WorkCorrectly()
    {
        var scope = new IdempotencyScope("invoices-issue");
        string stringValue = scope;
        stringValue.Should().Be("invoices-issue");

        var explicitScope = (IdempotencyScope)"invoices-issue";
        explicitScope.Value.Should().Be("invoices-issue");
        explicitScope.Should().Be(scope);
    }

    [Fact]
    public void ExplicitConversion_WithInvalidString_ThrowsException()
    {
        var actNull = () => (IdempotencyScope)(string)null!;
        actNull.Should().Throw<ArgumentException>();

        var actTooLong = () => (IdempotencyScope)new string('z', 70);
        actTooLong.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EqualityAndHashCode_WorkDeterministically()
    {
        var scope1 = new IdempotencyScope("my-scope");
        var scope2 = new IdempotencyScope("my-scope");
        var scope3 = new IdempotencyScope("other-scope");

        (scope1 == scope2).Should().BeTrue();
        (scope1 != scope2).Should().BeFalse();
        (scope1 == scope3).Should().BeFalse();
        (scope1 != scope3).Should().BeTrue();

        scope1.Equals(scope2).Should().BeTrue();
        scope1.Equals((object)scope2).Should().BeTrue();
        scope1.Equals(scope3).Should().BeFalse();
        scope1.Equals((object)scope3).Should().BeFalse();
        scope1.Equals(null).Should().BeFalse();
        scope1.Equals("not-a-scope").Should().BeFalse();

        scope1.GetHashCode().Should().Be(scope2.GetHashCode());
    }

    [Fact]
    public void ComparisonOperators_WorkDeterministically()
    {
        var scopeA = new IdempotencyScope("aaa");
        var scopeB = new IdempotencyScope("bbb");
        var scopeA2 = new IdempotencyScope("aaa");

        (scopeA < scopeB).Should().BeTrue();
        (scopeA <= scopeB).Should().BeTrue();
        (scopeB > scopeA).Should().BeTrue();
        (scopeB >= scopeA).Should().BeTrue();

        (scopeA <= scopeA2).Should().BeTrue();
        (scopeA >= scopeA2).Should().BeTrue();
        (scopeA < scopeA2).Should().BeFalse();
        (scopeA > scopeA2).Should().BeFalse();

        scopeA.CompareTo(scopeB).Should().BeNegative();
        scopeB.CompareTo(scopeA).Should().BePositive();
        scopeA.CompareTo(scopeA2).Should().Be(0);
    }

    [Fact]
    public void Comparison_IsCaseInsensitive()
    {
        var scopeLower = new IdempotencyScope("test-scope");
        var scopeUpper = new IdempotencyScope("TEST-SCOPE");

        scopeLower.CompareTo(scopeUpper).Should().Be(0);
    }

    [Fact]
    public void CompareToObject_WorksCorrectly()
    {
        var scopeA = new IdempotencyScope("aaa");
        var scopeB = new IdempotencyScope("bbb");

        scopeA.CompareTo((object)scopeB).Should().BeNegative();
        scopeB.CompareTo((object)scopeA).Should().BePositive();
        scopeA.CompareTo((object)new IdempotencyScope("aaa")).Should().Be(0);
        scopeA.CompareTo(null).Should().Be(1);

        var act = () => scopeA.CompareTo("some-string");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Object must be of type IdempotencyScope*");
    }
}
