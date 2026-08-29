// Copyright © Erickson Lopez. MIT License.
using AwesomeAssertions;
using EricksonLopez.Idempotency.AspNetCore;
using Xunit;

namespace EricksonLopez.Idempotency.AspNetCore.Tests;

public sealed class IdempotentAttributeTests
{
    [Fact]
    public void Defaults_And_PropertySetters_WorkCorrectly()
    {
        var attr = new IdempotentAttribute();

        attr.Scope.Should().BeNull();
        attr.Required.Should().BeTrue();
        attr.LeaseDurationSeconds.Should().Be(30);
        attr.RetentionDurationDays.Should().Be(7);
        attr.Enabled.Should().BeTrue();

        attr.Scope = "orders-custom";
        attr.Required = false;
        attr.LeaseDurationSeconds = 60;
        attr.RetentionDurationDays = 14;
        attr.Enabled = false;

        attr.Scope.Should().Be("orders-custom");
        attr.Required.Should().BeFalse();
        attr.LeaseDurationSeconds.Should().Be(60);
        attr.RetentionDurationDays.Should().Be(14);
        attr.Enabled.Should().BeFalse();
    }
}
