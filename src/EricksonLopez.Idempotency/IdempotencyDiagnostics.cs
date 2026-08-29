// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides unified OpenTelemetry activity tracing and metric meters for the idempotency framework.
/// </summary>
public static class IdempotencyDiagnostics
{
    /// <summary>
    /// Gets the service name used for OpenTelemetry meter and activity source registration.
    /// </summary>
    public const string ServiceName = "EricksonLopez.Idempotency";

    /// <summary>
    /// Gets the version string of the instrumentation library.
    /// </summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.ActivitySource"/> used for distributed tracing of idempotency operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.Metrics.Meter"/> used for exporting operational idempotency metrics.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // Metrics Counters
    private static readonly Counter<long> _requestsTotalCounter = Meter.CreateCounter<long>(
        "idempotency.requests",
        unit: "{request}",
        description: "Total number of idempotent operations processed.");

    private static readonly Counter<long> _duplicatesTotalCounter = Meter.CreateCounter<long>(
        "idempotency.duplicates",
        unit: "{duplicate}",
        description: "Total number of duplicate request attempts identified.");

    private static readonly Counter<long> _replayedTotalCounter = Meter.CreateCounter<long>(
        "idempotency.replayed",
        unit: "{replay}",
        description: "Total number of idempotent responses served from cache.");

    private static readonly Counter<long> _conflictsTotalCounter = Meter.CreateCounter<long>(
        "idempotency.conflicts",
        unit: "{conflict}",
        description: "Total number of in-flight concurrent idempotency conflicts.");

    private static readonly Counter<long> _executionsTotalCounter = Meter.CreateCounter<long>(
        "idempotency.executions",
        unit: "{execution}",
        description: "Total number of original underlying business executions performed.");

    private static readonly Counter<long> _completedTotalCounter = Meter.CreateCounter<long>(
        "idempotency.completed",
        unit: "{completed}",
        description: "Total number of idempotent operations successfully completed and cached.");

    private static readonly Counter<long> _failedTotalCounter = Meter.CreateCounter<long>(
        "idempotency.failed",
        unit: "{failed}",
        description: "Total number of idempotent operations marked failed.");

    private static readonly Counter<long> _fingerprintMismatchesCounter = Meter.CreateCounter<long>(
        "idempotency.fingerprint_mismatch",
        unit: "{mismatch}",
        description: "Total number of idempotency key reuse attempts with mismatched payload fingerprints.");

    // Metrics Histograms
    private static readonly Histogram<double> _durationHistogram = Meter.CreateHistogram<double>(
        "idempotency.duration",
        unit: "ms",
        description: "End-to-end execution duration of idempotent operations in milliseconds.");

    private static readonly Histogram<double> _storageLatencyHistogram = Meter.CreateHistogram<double>(
        "idempotency.storage_latency",
        unit: "ms",
        description: "Persistence store operation latency in milliseconds.");

    /// <summary>
    /// Records an incoming request attempt for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordRequest(string scope) => _requestsTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records a detected duplicate request attempt for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordDuplicate(string scope) => _duplicatesTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records a cached response replay served for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordReplayed(string scope) => _replayedTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records an in-flight concurrent execution conflict for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordConflict(string scope) => _conflictsTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records a newly executed underlying business operation for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordExecution(string scope) => _executionsTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records a successfully completed and cached idempotent operation for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordCompleted(string scope) => _completedTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records a failed idempotent operation for the specified functional scope.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordFailed(string scope) => _failedTotalCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records an idempotency key reuse collision with mismatched payload fingerprints.
    /// </summary>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordFingerprintMismatch(string scope) => _fingerprintMismatchesCounter.Add(1, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records the total end-to-end execution duration of an idempotent operation.
    /// </summary>
    /// <param name="milliseconds">The elapsed execution time in milliseconds.</param>
    /// <param name="scope">The functional partition scope of the operation.</param>
    public static void RecordDuration(double milliseconds, string scope) => _durationHistogram.Record(milliseconds, new KeyValuePair<string, object?>("scope", scope));

    /// <summary>
    /// Records the latency of a storage interaction operation.
    /// </summary>
    /// <param name="milliseconds">The elapsed storage operation time in milliseconds.</param>
    /// <param name="operation">The storage operation name.</param>
    public static void RecordStorageLatency(double milliseconds, string operation) => _storageLatencyHistogram.Record(milliseconds, new KeyValuePair<string, object?>("operation", operation));
}
