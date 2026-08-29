// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines policy rules and evaluation criteria for idempotent operation execution.
/// </summary>
public interface IIdempotencyPolicy
{
    /// <summary>
    /// Gets the duration an execution lease is granted before it can be stolen by another worker.
    /// </summary>
    TimeSpan LeaseDuration { get; }

    /// <summary>
    /// Gets the duration for which completed idempotency records are retained before becoming eligible for cleanup.
    /// </summary>
    TimeSpan RetentionDuration { get; }

    /// <summary>
    /// Gets a value indicating whether failed executions can be retried by acquiring a new lease.
    /// </summary>
    bool AllowRetryOnFailure { get; }

    /// <summary>
    /// Evaluates whether an HTTP or application status code is considered cacheable.
    /// </summary>
    /// <param name="statusCode">The status code to evaluate.</param>
    /// <returns><see langword="true"/> if the response should be stored; otherwise, <see langword="false"/>.</returns>
    bool IsCacheableStatusCode(int statusCode);
}
