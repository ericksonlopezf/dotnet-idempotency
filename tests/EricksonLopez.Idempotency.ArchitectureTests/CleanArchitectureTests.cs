// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.PostgreSql;
using NetArchTest.Rules;
using Xunit;

namespace EricksonLopez.Idempotency.ArchitectureTests;

/// <summary>
/// Architectural rules verifying Clean Architecture boundaries, dependency directions, and isolation across the ecosystem.
/// </summary>
public sealed class CleanArchitectureTests
{
    /// <summary>
    /// Verifies that Abstractions does not depend on any concrete infrastructure or web framework libraries.
    /// </summary>
    [Fact]
    public void Abstractions_MustNot_DependOn_InfrastructureOrWebFrameworks()
    {
        var result = Types.InAssembly(typeof(IIdempotencyStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "Dapper",
                "Microsoft.AspNetCore",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Mvc",
                "Microsoft.Data.SqlClient",
                "MySqlConnector",
                "Oracle.ManagedDataAccess",
                "Microsoft.Data.Sqlite")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "EricksonLopez.Idempotency.Abstractions must remain purely contract-based with zero infrastructure or presentation dependencies.");
    }

    /// <summary>
    /// Verifies that the Core Engine does not reference any concrete persistence or ASP.NET Core adapters.
    /// </summary>
    [Fact]
    public void CoreEngine_MustNot_DependOn_DatabaseOrAspNetCoreAdapters()
    {
        var result = Types.InAssembly(typeof(IdempotencyEngine).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "Dapper",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Mvc",
                "EricksonLopez.Idempotency.PostgreSql",
                "EricksonLopez.Idempotency.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "The core Idempotency engine must be completely decoupled from concrete persistence and web adapters.");
    }

    /// <summary>
    /// Verifies that the PostgreSQL adapter does not reference ASP.NET Core.
    /// </summary>
    [Fact]
    public void PostgreSqlAdapter_MustNot_DependOn_AspNetCore()
    {
        var result = Types.InAssembly(typeof(PostgreSqlIdempotencyStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.AspNetCore.Http",
                "EricksonLopez.Idempotency.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Persistence adapters must have zero knowledge of HTTP or ASP.NET Core presentation concerns.");
    }

    /// <summary>
    /// Verifies that the ASP.NET Core adapter does not reference concrete database storage providers.
    /// </summary>
    [Fact]
    public void AspNetCoreAdapter_MustNot_DependOn_ConcretePersistenceAdapters()
    {
        var result = Types.InAssembly(typeof(IdempotencyMiddleware).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "Dapper",
                "Microsoft.Data.SqlClient",
                "MySqlConnector",
                "Oracle.ManagedDataAccess",
                "EricksonLopez.Idempotency.PostgreSql",
                "EricksonLopez.Idempotency.SqlServer",
                "EricksonLopez.Idempotency.MySql",
                "EricksonLopez.Idempotency.MariaDb",
                "EricksonLopez.Idempotency.Oracle",
                "EricksonLopez.Idempotency.Sqlite")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "The ASP.NET Core adapter must only depend on Abstractions and never on concrete database implementations.");
    }
}
