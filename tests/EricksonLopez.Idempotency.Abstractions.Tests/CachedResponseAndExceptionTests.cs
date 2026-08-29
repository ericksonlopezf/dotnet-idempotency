// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using EricksonLopez.Idempotency.Exceptions;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Idempotency.Abstractions.Tests;

public sealed class CachedResponseAndExceptionTests
{
    [Fact]
    public void CachedIdempotencyResponse_SetsPropertiesCorrectly()
    {
        var headers = new Dictionary<string, string[]>
        {
            ["Content-Type"] = new[] { "application/json" },
            ["X-Custom-Header"] = new[] { "value1", "value2" }
        };
        var body = new byte[] { 1, 2, 3, 4, 5 };

        var cached = new CachedIdempotencyResponse(200, headers, body);

        cached.StatusCode.Should().Be(200);
        cached.Headers.Should().BeSameAs(headers);
        cached.Body.ToArray().Should().Equal(body);
        cached.ToString().Should().NotBeNullOrWhiteSpace();

        var cachedCopy = cached with { StatusCode = 201 };
        cachedCopy.StatusCode.Should().Be(201);
        (cached == cachedCopy).Should().BeFalse();
        (cached != cachedCopy).Should().BeTrue();
    }

    [Fact]
    public void IdempotencyClaimResult_HelperProperties_WorkCorrectly()
    {
        var token = Guid.NewGuid();
        var acquiredNew = new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, token, 1, null, null);
        acquiredNew.IsAcquired.Should().BeTrue();
        acquiredNew.IsReplay.Should().BeFalse();
        acquiredNew.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        acquiredNew.OwnerToken.Should().Be(token);
        acquiredNew.ConcurrencyVersion.Should().Be(1);
        acquiredNew.CachedResponse.Should().BeNull();
        acquiredNew.ExistingFingerprint.Should().BeNull();

        var acquiredStale = new IdempotencyClaimResult(ClaimResultStatus.AcquiredStale, token, 2, null, null);
        acquiredStale.IsAcquired.Should().BeTrue();
        acquiredStale.IsReplay.Should().BeFalse();

        var response = new CachedIdempotencyResponse(200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty);
        var replay = new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, response, null);
        replay.IsAcquired.Should().BeFalse();
        replay.IsReplay.Should().BeTrue();
        replay.CachedResponse.Should().BeSameAs(response);

        var inFlight = new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, null);
        inFlight.IsAcquired.Should().BeFalse();
        inFlight.IsReplay.Should().BeFalse();

        var mismatch = new IdempotencyClaimResult(ClaimResultStatus.FingerprintMismatch, null, null, null, "fp-old");
        mismatch.IsAcquired.Should().BeFalse();
        mismatch.IsReplay.Should().BeFalse();
        mismatch.ExistingFingerprint.Should().Be("fp-old");

        // Record equality and with-expressions
        var clone = acquiredNew with { ConcurrencyVersion = 99 };
        clone.ConcurrencyVersion.Should().Be(99);
        (acquiredNew == clone).Should().BeFalse();
        (acquiredNew != clone).Should().BeTrue();
        acquiredNew.ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IdempotencyContext_DefaultAndCustomValues_WorkCorrectly()
    {
        var context = new IdempotencyContext();
        context.TenantId.Should().Be(Guid.Empty);
        context.Key.Should().BeNull();
        context.Scope.Should().Be("default");
        context.OwnerToken.Should().BeNull();
        context.ConcurrencyVersion.Should().BeNull();
        context.IsReplay.Should().BeFalse();

        var tenantId = Guid.NewGuid();
        var ownerToken = Guid.NewGuid();
        var key = new IdempotencyKey("ctx-key");

        context.TenantId = tenantId;
        context.Key = key;
        context.Scope = "custom-scope";
        context.OwnerToken = ownerToken;
        context.ConcurrencyVersion = 42;
        context.IsReplay = true;

        context.TenantId.Should().Be(tenantId);
        context.Key.Should().Be(key);
        context.Scope.Should().Be("custom-scope");
        context.OwnerToken.Should().Be(ownerToken);
        context.ConcurrencyVersion.Should().Be(42);
        context.IsReplay.Should().BeTrue();
    }

    [Fact]
    public void IdempotencyOptions_DefaultAndCustomValues_WorkCorrectly()
    {
        var options = new IdempotencyOptions();

        options.Enabled.Should().BeTrue();
        options.RequireIdempotencyKey.Should().BeFalse();
        options.DefaultLeaseDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.DefaultRetentionDuration.Should().Be(TimeSpan.FromDays(7));
        options.MaxRequestBodySizeBytes.Should().Be(1024 * 1024);
        options.HeaderName.Should().Be("Idempotency-Key");
        options.StoreResponseBody.Should().BeTrue();
        options.CacheOnlySuccessResponses.Should().BeTrue();
        options.TenantIdExtractor.Should().BeNull();

        var expectedTenant = Guid.NewGuid();
        options.Enabled = false;
        options.RequireIdempotencyKey = true;
        options.DefaultLeaseDuration = TimeSpan.FromMinutes(1);
        options.DefaultRetentionDuration = TimeSpan.FromDays(30);
        options.MaxRequestBodySizeBytes = 2048;
        options.HeaderName = "X-Idempotency-Id";
        options.StoreResponseBody = false;
        options.CacheOnlySuccessResponses = false;
        options.TenantIdExtractor = _ => expectedTenant;

        options.Enabled.Should().BeFalse();
        options.RequireIdempotencyKey.Should().BeTrue();
        options.DefaultLeaseDuration.Should().Be(TimeSpan.FromMinutes(1));
        options.DefaultRetentionDuration.Should().Be(TimeSpan.FromDays(30));
        options.MaxRequestBodySizeBytes.Should().Be(2048);
        options.HeaderName.Should().Be("X-Idempotency-Id");
        options.StoreResponseBody.Should().BeFalse();
        options.CacheOnlySuccessResponses.Should().BeFalse();
        options.TenantIdExtractor(new object()).Should().Be(expectedTenant);
    }

    [Fact]
    public void IdempotencyException_AllConstructors_WorkCorrectly()
    {
        var defaultEx = new IdempotencyException();
        defaultEx.Message.Should().NotBeNull();
        defaultEx.InnerException.Should().BeNull();

        var msgEx = new IdempotencyException("custom error message");
        msgEx.Message.Should().Be("custom error message");
        msgEx.InnerException.Should().BeNull();

        var inner = new InvalidOperationException("inner error");
        var fullEx = new IdempotencyException("outer message", inner);
        fullEx.Message.Should().Be("outer message");
        fullEx.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void IdempotencyConflictException_SetsContextualProperties()
    {
        var conflict = new IdempotencyConflictException("k-conflict");
        conflict.Key.Should().Be("k-conflict");
        conflict.Message.Should().Contain("k-conflict");
        conflict.Should().BeAssignableTo<IdempotencyException>();
    }

    [Fact]
    public void IdempotencyFingerprintMismatchException_SetsContextualProperties()
    {
        var mismatch = new IdempotencyFingerprintMismatchException("k-mismatch", "fp-1", "fp-2");
        mismatch.Key.Should().Be("k-mismatch");
        mismatch.ExpectedFingerprint.Should().Be("fp-1");
        mismatch.ActualFingerprint.Should().Be("fp-2");
        mismatch.Message.Should().Contain("k-mismatch");
        mismatch.Message.Should().Contain("fp-1");
        mismatch.Message.Should().Contain("fp-2");
        mismatch.Should().BeAssignableTo<IdempotencyException>();
    }

    [Fact]
    public void IdempotencyLeaseExpiredException_SetsContextualProperties()
    {
        var expired = new IdempotencyLeaseExpiredException("k-expired");
        expired.Key.Should().Be("k-expired");
        expired.Message.Should().Contain("k-expired");
        expired.Should().BeAssignableTo<IdempotencyException>();
    }

    [Fact]
    public void Enums_HaveExpectedValues()
    {
        ((byte)ClaimResultStatus.AcquiredNew).Should().Be(1);
        ((byte)ClaimResultStatus.AcquiredStale).Should().Be(2);
        ((byte)ClaimResultStatus.CompletedReplay).Should().Be(3);
        ((byte)ClaimResultStatus.InFlightConflict).Should().Be(4);
        ((byte)ClaimResultStatus.FingerprintMismatch).Should().Be(5);

        ((byte)IdempotencyStatus.Processing).Should().Be(1);
        ((byte)IdempotencyStatus.Completed).Should().Be(2);
        ((byte)IdempotencyStatus.Failed).Should().Be(3);
    }
}
