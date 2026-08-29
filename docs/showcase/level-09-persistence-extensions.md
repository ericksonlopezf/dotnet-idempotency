# Level 09: Multi-Database Persistence Adapters

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Overview

`EricksonLopez.Idempotency` provides first-class persistence adapters across relational and key-value storage engines. Each provider implements the `IIdempotencyStore` SPI using native database atomicity primitives.

---

## 2. Storage Providers Matrix

| Provider Package | Database Engine | Concurrency / Atomicity Primitive | Native AOT | Transactional Support (`ITransactionalIdempotencyStore`) |
|---|---|---|---|---|
| `EricksonLopez.Idempotency.PostgreSql` | PostgreSQL 12+ | `INSERT ... ON CONFLICT DO NOTHING` + Dapper | ✅ 100% | ✅ Yes (`IDbConnection` + `IDbTransaction`) |
| `EricksonLopez.Idempotency.SqlServer` | SQL Server 2016+ / Azure SQL | `MERGE WITH (HOLDLOCK)` + Dapper | ✅ 100% | ✅ Yes (`IDbConnection` + `IDbTransaction`) |
| `EricksonLopez.Idempotency.MySql` | MySQL 8.0+ | `INSERT IGNORE INTO` + Dapper | ✅ 100% | ❌ Standard SPI only |
| `EricksonLopez.Idempotency.MariaDb` | MariaDB 10.5+ | `INSERT IGNORE INTO` + Dapper | ✅ 100% | ❌ Standard SPI only |
| `EricksonLopez.Idempotency.Sqlite` | SQLite 3 | `INSERT OR IGNORE INTO` + Dapper | ✅ 100% | ❌ Standard SPI only |
| `EricksonLopez.Idempotency.Oracle` | Oracle Database 19c+ | `MERGE INTO` + Dapper | ⚠️ Trimming limitations | ❌ Standard SPI only |
| `EricksonLopez.Idempotency.Redis` | Redis 6.0+ / Valkey | Lua Scripts on `IDatabase.ScriptEvaluateAsync` | ✅ 100% | ❌ Lua atomic execution |
| `EricksonLopez.Idempotency.Testing` | In-Memory (Test Doubles) | `ConcurrentDictionary<TKey, TValue>` | ✅ 100% | ❌ In-memory state |

---

## 3. Configuration Examples

### 1. PostgreSQL (Recommended for Enterprise)
```csharp
using EricksonLopez.Idempotency.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

services.AddNpgsqlDataSource(connectionString);
services.AddPostgreSqlIdempotencyStore();
```

### 2. Microsoft SQL Server
```csharp
using EricksonLopez.Idempotency.SqlServer;
using Microsoft.Extensions.DependencyInjection;

services.AddSqlServerIdempotencyStore("Server=sql.example.com;Database=OrdersDb;User Id=sa;Password=secret;");
```

### 3. Redis (Cloud-Native)
```csharp
using EricksonLopez.Idempotency.Redis;
using Microsoft.Extensions.DependencyInjection;

services.AddRedisIdempotency("redis.internal:6379", options =>
{
    options.KeyPrefix = "idemp:";
});
```

### 4. SQLite (Local Development / Edge)
```csharp
using EricksonLopez.Idempotency.Sqlite;
using Microsoft.Extensions.DependencyInjection;

services.AddSqliteIdempotencyStore("Data Source=idempotency.db;");
```

---

## 4. Next Steps

Proceed to [Level 10: ASP.NET Core & OpenTelemetry Observability](level-10-enterprise-architecture.md).
