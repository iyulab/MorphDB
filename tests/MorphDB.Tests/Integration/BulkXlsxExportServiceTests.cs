using ClosedXML.Excel;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Exercises <see cref="PostgresBulkOperationService"/>'s XLSX export directly against the write
/// pipeline and a real Postgres, bypassing the API's async job queue -- the queue's
/// <c>BulkJobProcessor</c> hosted service is deliberately removed under <c>ApiTestFixture</c> (it
/// polls tables that aren't ready yet at factory start-up), so the API-level export tests can only
/// ever observe a job stuck at "pending" and never actually exercise the byte output. This is the
/// one place the real fix for P2-o's XLSX defect (tab-delimited text served as
/// <c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet</c>) gets verified against
/// actual OOXML bytes.
/// </summary>
[Collection("PostgreSQL")]
public class BulkXlsxExportServiceTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly PostgresDataService _dataService;
    private readonly PostgresBulkOperationService _bulkService;

    public BulkXlsxExportServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        var metadataRepository = new MetadataRepository(fixture.DataSource);

        var nameHasher = new Sha256NameHasher();
        var lockManager = new PostgresAdvisoryLockManager(fixture.DataSource, new AdvisoryLockOptions());
        var changeLogger = new ChangeLogger(fixture.DataSource);

        _schemaManager = new PostgresSchemaManager(
            fixture.DataSource,
            metadataRepository,
            lockManager,
            nameHasher,
            changeLogger,
            new ProjectRepository(fixture.DataSource, new PostgresSchemaNameResolver()),
            new SchemaManagerOptions());

        _dataService = fixture.CreateDataService(
            metadataRepository,
            new SecurityPolicyService(fixture.DataSource),
            new SecurityContextAccessor());

        _bulkService = new PostgresBulkOperationService(
            fixture.DataSource, _schemaManager, _dataService, new BulkOperationOptions());
    }

    [Fact]
    public async Task StreamExportAsync_Xlsx_ProducesARealOoxmlWorkbookMatchingTheData()
    {
        var projectId = Guid.NewGuid();
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "xlsx_export_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "name", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "age", DataType = MorphDataType.Integer, IsNullable = true },
                new CreateColumnRequest { LogicalName = "is_active", DataType = MorphDataType.Boolean, IsNullable = false },
            ]
        });

        await _dataService.InsertBatchAsync(projectId, table.LogicalName,
        [
            new Dictionary<string, object?> { ["name"] = "Alice", ["age"] = 25, ["is_active"] = true },
            new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = 30, ["is_active"] = false },
        ]);

        var job = await _bulkService.StartXlsxExportAsync(projectId, table.LogicalName, new XlsxExportOptions
        {
            IncludeHeader = true,
            Columns = ["name", "age", "is_active"],
        });

        using var outputStream = new MemoryStream();
        await _bulkService.StreamExportAsync(job.JobId, outputStream);

        var bytes = outputStream.ToArray();

        // OOXML is a ZIP container; the pre-fix implementation wrote tab-delimited text, which
        // never starts with the ZIP local-file-header signature "PK".
        bytes.Length.Should().BeGreaterThan(2);
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var worksheet = workbook.Worksheets.First();

        worksheet.Cell(1, 1).GetString().Should().Be("name");
        worksheet.Cell(1, 2).GetString().Should().Be("age");
        worksheet.Cell(1, 3).GetString().Should().Be("is_active");
        worksheet.Cell(2, 1).GetString().Should().Be("Alice");
        worksheet.Cell(2, 2).GetDouble().Should().Be(25);
        worksheet.Cell(2, 3).GetBoolean().Should().BeTrue();
        worksheet.Cell(3, 1).GetString().Should().Be("Bob");
        worksheet.Cell(3, 3).GetBoolean().Should().BeFalse();
    }
}
