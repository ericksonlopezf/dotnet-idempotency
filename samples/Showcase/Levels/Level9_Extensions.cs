// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates multi-database persistence adapter configurations across all supported relational
/// and in-memory engines, including ITransactionalIdempotencyStore participation and Redis.
/// </summary>
public sealed class Level9Extensions : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 9 — Multi-Database Persistence Adapters";

    /// <inheritdoc/>
    public string Description => "Configuring all relational and Redis persistence providers via DI extension methods. All providers shown as code examples (real connections require external infrastructure).";

    /// <inheritdoc/>
    public Task ExecuteAsync()
    {
        Console.WriteLine("Available Storage Providers in EricksonLopez.Idempotency:\n");

        // ─── 1. PostgreSQL ────────────────────────────────────────────────────────
        Console.WriteLine("1. PostgreSQL (Npgsql + Dapper) — EricksonLopez.Idempotency.PostgreSql:");
        Console.WriteLine(@"
   // NpgsqlDataSource must be registered first (e.g., via Npgsql.DependencyInjection):
   builder.Services.AddNpgsqlDataSource(""Host=localhost;Database=mydb;Username=app;Password=secret"");

   // Then register the idempotency store (takes NpgsqlDataSource from DI):
   services.AddIdempotencyCore(options => { ... });
   services.AddPostgreSqlIdempotencyStore();

   Strategy: INSERT INTO idempotency_records (...) ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING
   Implements: ITransactionalIdempotencyStore (supports shared IDbConnection + IDbTransaction)
");

        // ─── 2. SQL Server ────────────────────────────────────────────────────────
        Console.WriteLine("2. SQL Server (Microsoft.Data.SqlClient + Dapper) — EricksonLopez.Idempotency.SqlServer:");
        Console.WriteLine(@"
   services.AddSqlServerIdempotencyStore(connectionString);

   Strategy: MERGE INTO idempotency_records WITH (HOLDLOCK) ...
             or IF NOT EXISTS atomic batch INSERT
   Implements: ITransactionalIdempotencyStore
");

        // ─── 3. MySQL ─────────────────────────────────────────────────────────────
        Console.WriteLine("3. MySQL (MySqlConnector + Dapper) — EricksonLopez.Idempotency.MySql:");
        Console.WriteLine(@"
   // MySqlDataSource must be registered first:
   services.AddSingleton(_ => new MySqlDataSourceBuilder(""Server=localhost;Database=mydb;Uid=app;Pwd=secret;"").Build());

   // Then register the idempotency store (takes MySqlDataSource from DI):
   services.AddMySqlIdempotencyStore();

   Strategy: INSERT IGNORE INTO idempotency_records (...)
   Implements: ITransactionalIdempotencyStore
");

        // ─── 4. MariaDB ───────────────────────────────────────────────────────────
        Console.WriteLine("4. MariaDB (MySqlConnector + Dapper) — EricksonLopez.Idempotency.MariaDb:");
        Console.WriteLine(@"
   // MySqlDataSource must be registered first (MariaDB uses MySqlConnector driver):
   services.AddSingleton(_ => new MySqlDataSourceBuilder(""Server=localhost;Database=mydb;Uid=app;Pwd=secret;"").Build());

   // Then register the idempotency store (takes MySqlDataSource from DI):
   services.AddMariaDbIdempotencyStore();

   Strategy: INSERT IGNORE INTO idempotency_records (...)
   Implements: ITransactionalIdempotencyStore
");

        // ─── 5. SQLite ────────────────────────────────────────────────────────────
        Console.WriteLine("5. SQLite (Microsoft.Data.Sqlite + Dapper) — EricksonLopez.Idempotency.Sqlite:");
        Console.WriteLine(@"
   services.AddSqliteIdempotencyStore(connectionString);

   Strategy: INSERT OR IGNORE INTO idempotency_records (...)
   Implements: ITransactionalIdempotencyStore
   Note: Ideal for local development, integration tests, and embedded scenarios.
");

        // ─── 6. Oracle ────────────────────────────────────────────────────────────
        Console.WriteLine("6. Oracle (Oracle.ManagedDataAccess.Core + Dapper) — EricksonLopez.Idempotency.Oracle:");
        Console.WriteLine(@"
   services.AddOracleIdempotencyStore(connectionString);

   Strategy: MERGE INTO idempotency_records USING DUAL ON (...)
             WHEN NOT MATCHED THEN INSERT ...
   Implements: ITransactionalIdempotencyStore
");

        // ─── 7. Redis ─────────────────────────────────────────────────────────────
        Console.WriteLine("7. Redis (StackExchange.Redis + Lua Scripts) — EricksonLopez.Idempotency.Redis:");
        Console.WriteLine(@"
   // Option A: provide connection string directly (registers IConnectionMultiplexer internally)
   services.AddRedisIdempotency(""localhost:6379"", options =>
   {
       options.KeyPrefix = ""idemp:"";
   });

   // Option B: register IConnectionMultiplexer externally and share it
   services.AddSingleton<IConnectionMultiplexer>(_ =>
       ConnectionMultiplexer.Connect(""localhost:6379""));
   services.AddRedisIdempotency(options =>
   {
       options.KeyPrefix = ""idemp:"";
   });

   Strategy: Atomic Lua scripts for TryAcquire, MarkCompleted, MarkFailed, Cleanup.
             Fencing token = INCR counter on Redis key.
   Note: Does NOT implement ITransactionalIdempotencyStore (Redis has no SQL transactions).
         Use for high-throughput, low-latency scenarios where SQL databases are a bottleneck.
");

        // ─── 8. ITransactionalIdempotencyStore — atomic participation ─────────────
        Console.WriteLine("8. ITransactionalIdempotencyStore — atomic Outbox coordination:");
        Console.WriteLine(@"
   // Pattern: check at runtime before calling transactional overload
   if (store is ITransactionalIdempotencyStore txStore)
   {
       // MarkCompletedAsync overload with IDbConnection + IDbTransaction:
       await txStore.MarkCompletedAsync(
           tenantId, scope, key, ownerToken, concurrencyVersion,
           statusCode, headers, responseBody, retentionDuration,
           connection, transaction, cancellationToken);

       // MarkFailedAsync overload with IDbConnection + IDbTransaction:
       await txStore.MarkFailedAsync(
           tenantId, scope, key, ownerToken, concurrencyVersion,
           connection, transaction, cancellationToken);
   }

   // Supported stores: PostgreSql, SqlServer, MySql, MariaDb, Sqlite, Oracle.
   // NOT supported: Redis (no SQL transaction boundary).
");

        // ─── 9. InMemoryIdempotencyStore — for testing ────────────────────────────
        Console.WriteLine("9. InMemoryIdempotencyStore (Testing package) — unit tests and local development:");
        Console.WriteLine(@"
   // Install: EricksonLopez.Idempotency.Testing
   var store = new InMemoryIdempotencyStore();
   // or with deterministic TimeProvider:
   var store = new InMemoryIdempotencyStore(fakeTimeProvider);

   // Reset state between test cases:
   store.Clear();

   // Does NOT implement ITransactionalIdempotencyStore.
   // Does NOT persist across process restarts.
");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] All persistence adapter configurations demonstrated.");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
