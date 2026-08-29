// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency;

/// <summary>
/// Represents RFC 9110 compliant problem details without runtime reflection.
/// </summary>
/// <param name="Type">The URI reference identifying the problem type.</param>
/// <param name="Title">The short, human-readable summary of the problem type.</param>
/// <param name="Status">The HTTP status code generated for this occurrence.</param>
/// <param name="Detail">The human-readable explanation specific to this occurrence.</param>
public sealed record IdempotencyProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail);
