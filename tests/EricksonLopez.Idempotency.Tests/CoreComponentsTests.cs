// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Idempotency.Tests;

public sealed class CoreComponentsTests
{
    [Fact]
    public void SystemTextJsonSerializer_DefaultAndCustomOptions_WorkCorrectly()
    {
        var serializer = new SystemTextJsonIdempotencySerializer();
        var payload = new TestPayload("SKU-1", 42);

        var bytes = serializer.Serialize(payload);
        var deserialized = serializer.Deserialize<TestPayload>(bytes);

        deserialized.Should().NotBeNull();
        deserialized!.Sku.Should().Be("SKU-1");
        deserialized.Quantity.Should().Be(42);

        // Verify case-insensitivity: lowercase json property maps to PascalCase property
        var caseMismatchJson = Encoding.UTF8.GetBytes("{\"sku\":\"LOWER-SKU\",\"quantity\":99}");
        var caseDeserialized = serializer.Deserialize<TestPayload>(caseMismatchJson);
        caseDeserialized.Should().NotBeNull();
        caseDeserialized!.Sku.Should().Be("LOWER-SKU");
        caseDeserialized.Quantity.Should().Be(99);

        var customOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var customSerializer = new SystemTextJsonIdempotencySerializer(customOptions);
        var customBytes = customSerializer.Serialize(payload);
        var customDeserialized = customSerializer.Deserialize<TestPayload>(customBytes);
        customDeserialized.Should().NotBeNull();
        customDeserialized!.Sku.Should().Be("SKU-1");

        var act = () => new SystemTextJsonIdempotencySerializer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void SystemTextJsonSerializer_SourceGeneratedTypes_SerializeCorrectly()
    {
        var serializer = new SystemTextJsonIdempotencySerializer();

        var dict = new Dictionary<string, string[]> { ["Content-Type"] = new[] { "application/json" } };
        var dictBytes = serializer.Serialize(dict);
        var dictDeserialized = serializer.Deserialize<Dictionary<string, string[]>>(dictBytes);
        dictDeserialized.Should().NotBeNull();
        dictDeserialized!["Content-Type"].Should().Contain("application/json");

        var response = new CachedIdempotencyResponse(200, dict, new byte[] { 1, 2, 3 });
        var respBytes = serializer.Serialize(response);
        var respDeserialized = serializer.Deserialize<CachedIdempotencyResponse>(respBytes);
        respDeserialized.Should().NotBeNull();
        respDeserialized!.StatusCode.Should().Be(200);

        var details = new IdempotencyProblemDetails("https://errors/conflict", "Conflict", 409, "In flight");
        var detailsBytes = serializer.Serialize(details);
        var detailsDeserialized = serializer.Deserialize<IdempotencyProblemDetails>(detailsBytes);
        detailsDeserialized.Should().NotBeNull();
        detailsDeserialized!.Status.Should().Be(409);
        detailsDeserialized.Title.Should().Be("Conflict");
    }

    [Fact]
    public void DefaultIdempotencyPolicy_ConstructorAndProperties_WorkCorrectly()
    {
        var options = new IdempotencyOptions
        {
            DefaultLeaseDuration = TimeSpan.FromSeconds(45),
            DefaultRetentionDuration = TimeSpan.FromDays(14)
        };
        var policy = new DefaultIdempotencyPolicy(options);

        policy.LeaseDuration.Should().Be(TimeSpan.FromSeconds(45));
        policy.RetentionDuration.Should().Be(TimeSpan.FromDays(14));
        policy.AllowRetryOnFailure.Should().BeTrue();

        policy.IsCacheableStatusCode(199).Should().BeFalse();
        policy.IsCacheableStatusCode(200).Should().BeTrue();
        policy.IsCacheableStatusCode(201).Should().BeTrue();
        policy.IsCacheableStatusCode(204).Should().BeTrue();
        policy.IsCacheableStatusCode(299).Should().BeTrue();
        policy.IsCacheableStatusCode(300).Should().BeFalse();
        policy.IsCacheableStatusCode(400).Should().BeFalse();
        policy.IsCacheableStatusCode(500).Should().BeFalse();

        var actNull = () => new DefaultIdempotencyPolicy(null!);
        actNull.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void AsyncLocalIdempotencyContextAccessor_GetAndSetContext()
    {
        var accessor = new AsyncLocalIdempotencyContextAccessor();
        accessor.IdempotencyContext.Should().BeNull();

        var context1 = new IdempotencyContext { Scope = "scope-1", TenantId = Guid.NewGuid() };
        accessor.IdempotencyContext = context1;
        accessor.IdempotencyContext.Should().BeSameAs(context1);

        var context2 = new IdempotencyContext { Scope = "scope-2", TenantId = Guid.NewGuid() };
        accessor.IdempotencyContext = context2;
        accessor.IdempotencyContext.Should().BeSameAs(context2);

        accessor.IdempotencyContext = null;
        accessor.IdempotencyContext.Should().BeNull();
    }

    [Fact]
    public void IdempotencyProblemDetails_PropertiesAndEquality()
    {
        var details = new IdempotencyProblemDetails("https://example.com/err", "Error Title", 409, "Detail text");

        details.Type.Should().Be("https://example.com/err");
        details.Title.Should().Be("Error Title");
        details.Status.Should().Be(409);
        details.Detail.Should().Be("Detail text");
        details.ToString().Should().NotBeNullOrWhiteSpace();

        var copy = details with { Status = 422 };
        (details == copy).Should().BeFalse();
        (details != copy).Should().BeTrue();
    }

    [Fact]
    public void OpenTelemetryDiagnostics_RecordAllInstruments_AndValidateWithMeterListener()
    {
        IdempotencyDiagnostics.ServiceName.Should().Be("EricksonLopez.Idempotency");
        IdempotencyDiagnostics.ServiceVersion.Should().Be("1.0.0");
        IdempotencyDiagnostics.ActivitySource.Should().NotBeNull();
        IdempotencyDiagnostics.Meter.Should().NotBeNull();

        var recordedMeasurements = new List<(string InstrumentName, object Value, KeyValuePair<string, object?> Tag)>();
        var publishedInstruments = new Dictionary<string, (string? Unit, string? Description)>();
        var lockObj = new object();

        using (var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == IdempotencyDiagnostics.ServiceName)
                {
                    lock (lockObj)
                    {
                        publishedInstruments[instrument.Name] = (instrument.Unit, instrument.Description);
                    }
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        })
        {
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                lock (lockObj)
                {
                    var firstTag = tags.Length > 0 ? tags[0] : default;
                    recordedMeasurements.Add((instrument.Name, measurement, firstTag));
                }
            });

            listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                lock (lockObj)
                {
                    var firstTag = tags.Length > 0 ? tags[0] : default;
                    recordedMeasurements.Add((instrument.Name, measurement, firstTag));
                }
            });

            listener.Start();

            IdempotencyDiagnostics.RecordRequest("orders");
            IdempotencyDiagnostics.RecordDuplicate("orders");
            IdempotencyDiagnostics.RecordReplayed("orders");
            IdempotencyDiagnostics.RecordConflict("orders");
            IdempotencyDiagnostics.RecordExecution("orders");
            IdempotencyDiagnostics.RecordCompleted("orders");
            IdempotencyDiagnostics.RecordFailed("orders");
            IdempotencyDiagnostics.RecordFingerprintMismatch("orders");
            IdempotencyDiagnostics.RecordDuration(12.5, "orders");
            IdempotencyDiagnostics.RecordStorageLatency(3.2, "TryAcquire");
        }

        publishedInstruments["idempotency.requests"].Unit.Should().Be("{request}");
        publishedInstruments["idempotency.requests"].Description.Should().Be("Total number of idempotent operations processed.");

        publishedInstruments["idempotency.duplicates"].Unit.Should().Be("{duplicate}");
        publishedInstruments["idempotency.duplicates"].Description.Should().Be("Total number of duplicate request attempts identified.");

        publishedInstruments["idempotency.replayed"].Unit.Should().Be("{replay}");
        publishedInstruments["idempotency.replayed"].Description.Should().Be("Total number of idempotent responses served from cache.");

        publishedInstruments["idempotency.conflicts"].Unit.Should().Be("{conflict}");
        publishedInstruments["idempotency.conflicts"].Description.Should().Be("Total number of in-flight concurrent idempotency conflicts.");

        publishedInstruments["idempotency.executions"].Unit.Should().Be("{execution}");
        publishedInstruments["idempotency.executions"].Description.Should().Be("Total number of original underlying business executions performed.");

        publishedInstruments["idempotency.completed"].Unit.Should().Be("{completed}");
        publishedInstruments["idempotency.completed"].Description.Should().Be("Total number of idempotent operations successfully completed and cached.");

        publishedInstruments["idempotency.failed"].Unit.Should().Be("{failed}");
        publishedInstruments["idempotency.failed"].Description.Should().Be("Total number of idempotent operations marked failed.");

        publishedInstruments["idempotency.fingerprint_mismatch"].Unit.Should().Be("{mismatch}");
        publishedInstruments["idempotency.fingerprint_mismatch"].Description.Should().Be("Total number of idempotency key reuse attempts with mismatched payload fingerprints.");

        publishedInstruments["idempotency.duration"].Unit.Should().Be("ms");
        publishedInstruments["idempotency.duration"].Description.Should().Be("End-to-end execution duration of idempotent operations in milliseconds.");

        publishedInstruments["idempotency.storage_latency"].Unit.Should().Be("ms");
        publishedInstruments["idempotency.storage_latency"].Description.Should().Be("Persistence store operation latency in milliseconds.");

        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.requests" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.duplicates" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.replayed" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.conflicts" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.executions" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.completed" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.failed" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.fingerprint_mismatch" && (long)m.Value == 1 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.duration" && (double)m.Value == 12.5 && m.Tag.Key == "scope" && (string)m.Tag.Value! == "orders");
        recordedMeasurements.Should().Contain(m => m.InstrumentName == "idempotency.storage_latency" && (double)m.Value == 3.2 && m.Tag.Key == "operation" && (string)m.Tag.Value! == "TryAcquire");
    }

    public sealed record TestPayload(string Sku, int Quantity);
}
