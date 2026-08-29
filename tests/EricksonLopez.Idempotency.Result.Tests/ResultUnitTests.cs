// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Result;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Idempotency.Result.Tests;

public sealed class ResultUnitTests
{
    [Fact]
    public void IdempotencyErrors_InFlightConflict_ReturnsExpectedErrorCodeAndDescription()
    {
        var error = IdempotencyErrors.InFlightConflict("order-key-1");

        error.Code.Should().Be("Idempotency.InFlightConflict");
        error.Description.Should().Be("An identical operation with idempotency key 'order-key-1' is currently being processed.");
        error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void IdempotencyErrors_FingerprintMismatch_ReturnsExpectedErrorCodeAndDescription()
    {
        var error = IdempotencyErrors.FingerprintMismatch("order-key-2");

        error.Code.Should().Be("Idempotency.FingerprintMismatch");
        error.Description.Should().Be("The idempotency key 'order-key-2' was previously used with a different request payload.");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void IdempotencyErrors_LeaseLost_ReturnsExpectedErrorCodeAndDescription()
    {
        var error = IdempotencyErrors.LeaseLost("order-key-3");

        error.Code.Should().Be("Idempotency.LeaseLost");
        error.Description.Should().Be("Ownership lease for idempotency key 'order-key-3' was lost before completion.");
        error.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void AsErrorResult_WhenInFlightConflict_ReturnsConflictErrorResult()
    {
        var claim = new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, null);

        var errorResult = claim.AsErrorResult<string>("k-conflict");

        errorResult.Should().NotBeNull();
        var result = errorResult!.Value;
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.InFlightConflict");
        result.Error.Description.Should().Be("An identical operation with idempotency key 'k-conflict' is currently being processed.");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void AsErrorResult_WhenFingerprintMismatch_ReturnsValidationErrorResult()
    {
        var claim = new IdempotencyClaimResult(ClaimResultStatus.FingerprintMismatch, null, null, null, "fp-old");

        var errorResult = claim.AsErrorResult<int>("k-mismatch");

        errorResult.Should().NotBeNull();
        var result = errorResult!.Value;
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.FingerprintMismatch");
        result.Error.Description.Should().Be("The idempotency key 'k-mismatch' was previously used with a different request payload.");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData(ClaimResultStatus.AcquiredNew)]
    [InlineData(ClaimResultStatus.AcquiredStale)]
    [InlineData(ClaimResultStatus.CompletedReplay)]
    public void AsErrorResult_WhenSuccessfulOrReplayClaim_ReturnsNull(ClaimResultStatus status)
    {
        var claim = new IdempotencyClaimResult(status, Guid.NewGuid(), 1, null, null);

        var errorResult = claim.AsErrorResult<string>("k-valid");

        errorResult.Should().BeNull();
    }
}
