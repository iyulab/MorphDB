using System.Net.Http.Json;
using Dapper;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The truth anchor of the constraint-boundary gate: it declares one table exercising every
/// constraint kind, then reads the database catalogs back to see which of them the DDL actually
/// emitted, and holds the ARCHITECTURE table to that observation. Its companion
/// (ConstraintBoundaryDocParityTests) compares the other documents to the same table, so the whole
/// boundary is pinned to observed behaviour with no hand-written inventory anywhere — a list
/// maintained by hand would only restate the claim, and a restated claim is what drifted in the
/// first place.
///
/// Each kind lives in a different catalog, so one query per kind: constraints in pg_constraint,
/// NOT NULL as a column attribute, DEFAULT as a column default, indexes in pg_indexes. Querying
/// pg_constraint alone would report NOT NULL, DEFAULT and Index as absent and manufacture a
/// mismatch that is not there.
///
/// Two things this deliberately does not cover. CHECK is owned by
/// CheckDeclarationContractTests.A_supported_check_is_accepted_and_emits_no_physical_constraint —
/// asserting it twice would mean two places to update. And the foreign-key observation is made
/// with the enforcing default: the many-to-many carve-out and the enforceOnWrite=false path (which
/// emits no physical constraint either, by design) are stated in ARCHITECTURE but are not what
/// this test measures.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ConstraintBoundaryContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ConstraintBoundaryContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task The_architecture_table_matches_what_the_database_actually_enforces()
    {
        var observed = await ObservePhysicalConstraintsAsync();
        var documented = ConstraintBoundaryDoc.PhysicalNames(
            ConstraintBoundaryDoc.ReadRepoFile("docs/ARCHITECTURE.md"));

        observed.Should().BeSubsetOf(documented,
            "a constraint the database enforces but the table calls virtual tells a reader the " +
            "application is the only thing standing between them and bad data");

        documented.Except(["CHECK"]).Should().BeSubsetOf(observed,
            "a constraint the table calls physical but the database never emitted is a ghost " +
            "contract — CHECK is excluded because its virtuality is asserted by " +
            "CheckDeclarationContractTests, not here");
    }

    /// <summary>
    /// Declares a table whose columns and relation exercise PK, Index, UNIQUE, NOT NULL, DEFAULT
    /// and FK, then reports which of those names the physical schema carries.
    /// </summary>
    private async Task<HashSet<string>> ObservePhysicalConstraintsAsync()
    {
        var suffix = $"{Guid.NewGuid():N}"[..8];
        var target = $"cbt_t_{suffix}";
        var source = $"cbt_s_{suffix}";

        await EnsureDeclaredAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = target,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true }],
        }));

        var sourceResponse = await EnsureDeclaredAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = source,
            Columns =
            [
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false, Indexed = true },
                new CreateColumnApiRequest { Name = "tier", Type = "text", Default = "'standard'" },
                new CreateColumnApiRequest { Name = "target_ref", Type = "uuid" },
            ],
        }));
        var sourceTable = await sourceResponse.Content.ReadFromJsonAsync<TableApiResponse>();

        await EnsureDeclaredAsync(_client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"cbt_rel_{suffix}",
            SourceTable = source,
            SourceColumn = "target_ref",
            TargetTable = target,
            TargetColumn = "_id",
        }));

        var physicalName = await PhysicalNameAsync(sourceTable!.Id);
        var targetPhysicalName = await PhysicalNameByLogicalAsync(target);

        // Logical names are not physical names — that separation is the product. Asking the
        // catalogs about a logical name silently returns nothing, which reads exactly like an
        // absent constraint.
        var notNullColumn = await PhysicalColumnNameAsync(sourceTable.Id, "label");
        var defaultColumn = await PhysicalColumnNameAsync(sourceTable.Id, "tier");

        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);

        var present = new HashSet<string>(StringComparer.Ordinal);

        // PK, UNIQUE and FK are constraint rows; the target table carries the UNIQUE one.
        var constraintTypes = (await connection.QueryAsync<char>(
            """
            SELECT con.contype FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            WHERE rel.relname IN (@physicalName, @targetPhysicalName)
            """,
            new { physicalName, targetPhysicalName })).ToHashSet();

        if (constraintTypes.Contains('p'))
        {
            present.Add("PRIMARY KEY");
        }

        if (constraintTypes.Contains('u'))
        {
            present.Add("UNIQUE");
        }

        if (constraintTypes.Contains('f'))
        {
            present.Add("FOREIGN KEY");
        }

        // NOT NULL is a column attribute, not a constraint row.
        var notNullCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = @physicalName AND column_name = @notNullColumn AND is_nullable = 'NO'
            """,
            new { physicalName, notNullColumn });
        if (notNullCount > 0)
        {
            present.Add("NOT NULL");
        }

        // DEFAULT is a column default expression.
        var defaultCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = @physicalName AND column_name = @defaultColumn AND column_default IS NOT NULL
            """,
            new { physicalName, defaultColumn });
        if (defaultCount > 0)
        {
            present.Add("DEFAULT");
        }

        // Indexes live in their own catalog.
        var indexCount = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_indexes WHERE tablename = @physicalName",
            new { physicalName });
        if (indexCount > 0)
        {
            present.Add("INDEX");
        }

        return present;
    }

    /// <summary>
    /// Fails with the server's own message rather than a bare status code — a declaration this
    /// test got wrong is indistinguishable from a regression otherwise.
    /// </summary>
    private static async Task<HttpResponseMessage> EnsureDeclaredAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"declaration refused with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return response;
    }

    private Task<string> PhysicalColumnNameAsync(Guid tableId, string logicalName) => QuerySingleAsync(
        """
        SELECT physical_name FROM morphdb._morph_columns
        WHERE table_id = @tableId AND logical_name = @logicalName AND is_active = true
        """,
        new { tableId, logicalName });

    private Task<string> PhysicalNameAsync(Guid tableId) => QuerySingleAsync(
        "SELECT physical_name FROM morphdb._morph_tables WHERE table_id = @tableId", new { tableId });

    private Task<string> PhysicalNameByLogicalAsync(string logicalName) => QuerySingleAsync(
        "SELECT physical_name FROM morphdb._morph_tables WHERE logical_name = @logicalName AND is_active = true",
        new { logicalName });

    private async Task<string> QuerySingleAsync(string sql, object parameters)
    {
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        return await connection.QuerySingleAsync<string>(sql, parameters);
    }
}
