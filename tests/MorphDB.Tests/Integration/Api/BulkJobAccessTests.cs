using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Security;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A bulk job is there for a caller working in its project that requested it, or that presents the
/// master secret — and for nobody else. An export's file holds the rows its requester may read, so
/// a job that answers anyone who knows its id hands those rows past every policy.
/// <para>
/// Each test reaches one job through the routes that take a job id, as five callers: its requester,
/// the master secret, another caller of the same project, and the requester's own secret and the
/// master secret addressing another project. The last three must be answered exactly as an id that
/// was never issued is — not refused, which would say the job exists. The callers come from a host
/// that enforces secrets: that is where one project has callers that differ.
/// </para>
/// <para>
/// The test host does not run the job processor. A job is carried out here the way the processor
/// carries it out: found among the pending jobs, and handed to the method the processor's loop
/// calls for each job it finds — from a test, which like the processor has no caller.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkJobAccessTests
{
    // The secret SecretEnforcementTests injects. The fixture keeps one enforced host per distinct
    // secret, so naming the same one here does not start another.
    private const string MasterSecret = "mdb_test_master_secret_value";

    private const string ExportJobs = "_morph_export_jobs";
    private const string ImportJobs = "_morph_import_jobs";

    private readonly ApiIntegrationFixture _fixture;
    private readonly RowLevelSecurityHarness _rls;

    public BulkJobAccessTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _rls = new RowLevelSecurityHarness(fixture.Postgres);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ExportStatus(Guid jobId) => $"/api/bulk/export/{jobId}";
    private static string ImportStatus(Guid jobId) => $"/api/bulk/import/{jobId}";
    private static string Progress(Guid jobId) => $"/api/bulk/jobs/{jobId}/progress";
    private static string Cancel(Guid jobId) => $"/api/bulk/jobs/{jobId}/cancel";
    private static string Download(Guid jobId) => $"/api/bulk/export/{jobId}/download";

    /// <summary>
    /// A table in the fixture's project holding two rows of A's and one of B's, under a policy that
    /// shows each caller its own — and the five callers a job of A's is reached as.
    /// </summary>
    private sealed record World(
        string Table,
        string OwnerA,
        string OwnerB,
        HttpClient Requester,
        HttpClient AnotherCaller,
        HttpClient Master,
        HttpClient RequesterElsewhere,
        HttpClient MasterElsewhere)
    {
        /// <summary>Callers the job is not there for.</summary>
        public IEnumerable<(string Who, HttpClient Client)> Outsiders =>
        [
            ("another caller of the project", AnotherCaller),
            ("the requester's secret addressing another project", RequesterElsewhere),
            ("the master secret addressing another project", MasterElsewhere),
        ];

        /// <summary>Callers the job is there for.</summary>
        public IEnumerable<(string Who, HttpClient Client)> Insiders =>
        [
            ("the requester", Requester),
            ("the master secret", Master),
        ];
    }

    private HttpClient Enforced(string secret, Guid projectId)
    {
        var client = _fixture.Api.WithMasterSecret(MasterSecret).CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", projectId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private async Task<World> CreateWorldAsync()
    {
        var home = _fixture.Api.ProjectId;
        var master = Enforced(MasterSecret, home);

        var created = await master.PostAsJsonAsync(
            "/api/projects", new CreateProjectApiRequest { Name = $"jobs_{Guid.NewGuid():N}"[..28] }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var elsewhere = (await created.Content.ReadFromJsonAsync<ProjectApiResponse>(Ct))!.Id;

        var a = await IssueAsync(master, "requester");
        var b = await IssueAsync(master, "another");

        var table = await CreateTableAsync(master);
        await InsertAsync(master, table, "a-1", a.SecretId.ToString());
        await InsertAsync(master, table, "a-2", a.SecretId.ToString());
        await InsertAsync(master, table, "b-1", b.SecretId.ToString());

        var policy = await master.PostAsJsonAsync("/api/security/policies", new
        {
            name = "own_rows",
            tableName = table,
            policyType = "Select",
            expression = "owner = {{user_id}}"
        }, Ct);
        policy.StatusCode.Should().Be(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync(Ct));

        return new World(
            table,
            a.SecretId.ToString(),
            b.SecretId.ToString(),
            Requester: Enforced(a.Secret, home),
            AnotherCaller: Enforced(b.Secret, home),
            Master: master,
            RequesterElsewhere: Enforced(a.Secret, elsewhere),
            MasterElsewhere: Enforced(MasterSecret, elsewhere));
    }

    private static async Task<IssuedSecretResponse> IssueAsync(HttpClient master, string label)
    {
        var response = await master.PostAsJsonAsync("/api/security/secrets", new IssueSecretApiRequest
        {
            Name = $"{label}-{Guid.NewGuid():N}",
            Role = "writer"
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<IssuedSecretResponse>(Ct))!;
    }

    private static async Task<string> CreateTableAsync(HttpClient client)
    {
        var table = $"job_access_{Guid.NewGuid():N}"[..30];
        var response = await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "owner", Type = "text", Nullable = true }
            ]
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return table;
    }

    private static async Task InsertAsync(HttpClient client, string table, string name, string? owner = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/data/{table}", new Dictionary<string, object?> { ["name"] = name, ["owner"] = owner }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Requests an export of the names alone, so each line of the file is one row's name.</summary>
    private static async Task<Guid> RequestExportAsync(HttpClient client, string table)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/bulk/{table}/export/csv", new CsvExportApiRequest { Columns = ["name"] }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ExportJobApiResponse>(Ct))!.JobId;
    }

    private static async Task<Guid> RequestImportAsync(HttpClient client, string table, params (string Name, string Owner)[] rows)
    {
        var json = "[" + string.Join(",", rows.Select(r => $$"""{"name":"{{r.Name}}","owner":"{{r.Owner}}"}""")) + "]";
        var response = await client.PostAsync(
            $"/api/bulk/{table}/import/json", new StringContent(json, Encoding.UTF8, "application/json"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportJobApiResponse>(Ct))!.JobId;
    }

    private IBulkJobExecutor Executor => _fixture.Api.Services.GetRequiredService<IBulkJobExecutor>();

    /// <summary>
    /// Carries out pending export jobs as the job processor does — each found among the pending
    /// jobs of every project and requester, run with no caller, its file stored.
    /// </summary>
    private Task CarryOutExportsAsync(params Guid[] jobIds) => As(null, async () =>
    {
        var pending = await Executor.GetPendingExportJobsAsync(int.MaxValue, Ct);
        foreach (var jobId in jobIds)
        {
            var job = pending.Should().ContainSingle(j => j.JobId == jobId, "the processor finds every pending job").Which;
            await _rls.JobProcessor.ProcessExportJobAsync(Executor, job, Ct);
        }
    });

    /// <inheritdoc cref="CarryOutExportsAsync"/>
    private Task CarryOutImportsAsync(params Guid[] jobIds) => As(null, async () =>
    {
        var pending = await Executor.GetPendingImportJobsAsync(int.MaxValue, Ct);
        foreach (var jobId in jobIds)
        {
            var job = pending.Should().ContainSingle(j => j.JobId == jobId, "the processor finds every pending job").Which;
            await _rls.JobProcessor.ProcessImportJobAsync(Executor, job, Ct);
        }
    });

    /// <summary>
    /// Leaves an export job completed with no file stored — the state in which a download reads the
    /// table when it is asked for.
    /// </summary>
    private async Task CompleteWithoutStoringAsync(Guid jobId)
    {
        using var discarded = new MemoryStream();
        await As(null, () => Executor.StreamExportAsync(jobId, discarded, Ct));
        (await _rls.StoredExportFileAsync(jobId)).Should().BeNull();
    }

    /// <summary>
    /// The request is answered as the same request for an id that was never issued: the same status
    /// and, but for the id it repeats, the same body.
    /// </summary>
    private static async Task ShouldBeNoSuchJobAsync(
        string who, HttpClient client, HttpMethod method, Func<Guid, string> route, Guid jobId)
    {
        var neverIssued = Guid.NewGuid();
        using var forTheJob = await client.SendAsync(new HttpRequestMessage(method, route(jobId)), Ct);
        using var forNoJob = await client.SendAsync(new HttpRequestMessage(method, route(neverIssued)), Ct);

        var what = $"{who}: {method} {route(jobId)}";
        forNoJob.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forTheJob.StatusCode.Should().Be(HttpStatusCode.NotFound, what);

        var body = await forTheJob.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("JOB_NOT_FOUND", what);
        body.Replace(jobId.ToString(), "<id>", StringComparison.Ordinal).Should().Be(
            (await forNoJob.Content.ReadAsStringAsync(Ct)).Replace(neverIssued.ToString(), "<id>", StringComparison.Ordinal),
            $"{what} must not be told apart from an unknown id");
    }

    private static async Task<string> ExportStateAsync(HttpClient client, Guid jobId) =>
        (await client.GetFromJsonAsync<ExportJobApiResponse>(ExportStatus(jobId), Ct))!.Status;

    private static async Task<ImportJobApiResponse> ImportJobAsync(HttpClient client, Guid jobId) =>
        (await client.GetFromJsonAsync<ImportJobApiResponse>(ImportStatus(jobId), Ct))!;

    private static async Task<List<Guid>> ListedExportsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ExportJobApiResponse>>("/api/bulk/export?limit=1000", Ct))!
            .Select(j => j.JobId).ToList();

    private static async Task<List<Guid>> ListedImportsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ImportJobApiResponse>>("/api/bulk/import?limit=1000", Ct))!
            .Select(j => j.JobId).ToList();

    /// <summary>Downloads the job's file and answers its lines without the header: the names it holds.</summary>
    private static async Task<List<string>> DownloadedNamesAsync(string who, HttpClient client, Guid jobId)
    {
        using var response = await client.GetAsync(Download(jobId), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{who} downloads: {await response.Content.ReadAsStringAsync(Ct)}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");

        return (await response.Content.ReadAsStringAsync(Ct))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .ToList();
    }

    // ---- the matrix: an export job of A's, reached five ways ----------------------------------

    [Fact]
    public async Task The_status_of_an_export_job_answers_only_its_requester_and_the_master_secret_in_its_project()
    {
        var world = await CreateWorldAsync();
        var job = await RequestExportAsync(world.Requester, world.Table);

        foreach (var (who, client) in world.Outsiders)
        {
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, ExportStatus, job);
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Progress, job);
            (await ListedExportsAsync(client)).Should().NotContain(job, $"{who} lists export jobs");
        }

        foreach (var (who, client) in world.Insiders)
        {
            var status = await client.GetFromJsonAsync<ExportJobApiResponse>(ExportStatus(job), Ct);
            status!.JobId.Should().Be(job, who);
            status.TotalRows.Should().Be(2, "the job counted the rows its requester may read");

            var progress = await client.GetFromJsonAsync<JobProgressApiResponse>(Progress(job), Ct);
            progress!.JobId.Should().Be(job, who);

            (await ListedExportsAsync(client)).Should().Contain(job, $"{who} lists export jobs");
        }
    }

    [Fact]
    public async Task An_export_job_is_cancelled_only_by_its_requester_or_the_master_secret_in_its_project()
    {
        var world = await CreateWorldAsync();
        var job = await RequestExportAsync(world.Requester, world.Table);

        foreach (var (who, client) in world.Outsiders)
        {
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Post, Cancel, job);
        }

        (await ExportStateAsync(world.Master, job)).Should().Be("pending", "nobody the job is not there for has cancelled it");

        foreach (var (who, client) in world.Insiders)
        {
            var own = await RequestExportAsync(world.Requester, world.Table);
            (await client.PostAsync(Cancel(own), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent, who);
            (await ExportStateAsync(world.Master, own)).Should().Be("cancelled", who);
        }
    }

    [Fact]
    public async Task A_stored_export_file_is_served_only_to_its_requester_or_the_master_secret_and_holds_the_requesters_rows()
    {
        var world = await CreateWorldAsync();
        var job = await RequestExportAsync(world.Requester, world.Table);

        // While the job is pending: a caller it is not there for is not told that it has not
        // finished, which would be telling it that the job exists.
        foreach (var (who, client) in world.Outsiders)
        {
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Download, job);
        }

        using (var early = await world.Requester.GetAsync(Download(job), Ct))
        {
            early.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await early.Content.ReadAsStringAsync(Ct)).Should().Contain("JOB_NOT_COMPLETED");
        }

        await CarryOutExportsAsync(job);
        (await _rls.StoredExportFileAsync(job)).Should().NotBeNull("the processor stored the file");

        foreach (var (who, client) in world.Outsiders)
        {
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Download, job);
        }

        foreach (var (who, client) in world.Insiders)
        {
            (await DownloadedNamesAsync(who, client, job)).Should().BeEquivalentTo(
                ["a-1", "a-2"], $"{who} is served the file as it was stored: the rows its requester may read");
        }
    }

    [Fact]
    public async Task An_export_read_when_it_is_downloaded_is_served_only_to_its_requester_or_the_master_secret_and_read_as_the_requester()
    {
        var world = await CreateWorldAsync();
        var job = await RequestExportAsync(world.Requester, world.Table);
        await CompleteWithoutStoringAsync(job);

        foreach (var (who, client) in world.Outsiders)
        {
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Download, job);
        }

        foreach (var (who, client) in world.Insiders)
        {
            (await DownloadedNamesAsync(who, client, job)).Should().BeEquivalentTo(
                ["a-1", "a-2"],
                $"the table is read as the job's requester, not as {who} — the master secret reads all three rows for itself");
        }

        (await _rls.StoredExportFileAsync(job)).Should().BeNull("each download read the table; none was served a stored file");
    }

    [Fact]
    public async Task An_import_job_answers_and_is_cancelled_only_by_its_requester_or_the_master_secret_in_its_project()
    {
        var world = await CreateWorldAsync();
        var job = await RequestImportAsync(world.Requester, world.Table, ("a-3", world.OwnerA));

        foreach (var (who, client) in world.Outsiders)
        {
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, ImportStatus, job);
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Progress, job);
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Post, Cancel, job);
            (await ListedImportsAsync(client)).Should().NotContain(job, $"{who} lists import jobs");
        }

        (await ImportJobAsync(world.Master, job)).Status.Should().Be("pending");

        foreach (var (who, client) in world.Insiders)
        {
            (await ImportJobAsync(client, job)).JobId.Should().Be(job, who);
            (await client.GetFromJsonAsync<JobProgressApiResponse>(Progress(job), Ct))!.JobId.Should().Be(job, who);
            (await ListedImportsAsync(client)).Should().Contain(job, $"{who} lists import jobs");

            var own = await RequestImportAsync(world.Requester, world.Table, ("a-4", world.OwnerA));
            (await client.PostAsync(Cancel(own), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent, who);
            (await ImportJobAsync(world.Master, own)).Status.Should().Be("cancelled", who);
        }
    }

    // ---- a job whose row holds no requester ----------------------------------------------------

    /// <summary>
    /// A job stored by a version that did not record who requested it. Nobody can be shown to be
    /// its requester, so it is there for the master secret alone — and what it reads, it reads as
    /// nobody: under a policy that compares with the caller, its file holds no row.
    /// </summary>
    [Fact]
    public async Task A_job_with_no_recorded_requester_is_there_only_for_the_master_secret()
    {
        var world = await CreateWorldAsync();

        var stored = await RequestExportAsync(world.Requester, world.Table);
        var readWhenDownloaded = await RequestExportAsync(world.Requester, world.Table);
        var pending = await RequestExportAsync(world.Requester, world.Table);
        var import = await RequestImportAsync(world.Requester, world.Table, ("a-3", world.OwnerA));

        foreach (var export in new[] { stored, readWhenDownloaded, pending })
        {
            await _rls.ForgetRequesterAsync(ExportJobs, export);
        }

        await _rls.ForgetRequesterAsync(ImportJobs, import);
        await CarryOutExportsAsync(stored);
        await CompleteWithoutStoringAsync(readWhenDownloaded);

        var everyoneElse = world.Outsiders.Prepend(("the caller that requested it", world.Requester));
        foreach (var (who, client) in everyoneElse)
        {
            foreach (var export in new[] { stored, readWhenDownloaded, pending })
            {
                await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, ExportStatus, export);
                await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Progress, export);
                await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, Download, export);
                await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Post, Cancel, export);
            }

            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Get, ImportStatus, import);
            await ShouldBeNoSuchJobAsync(who, client, HttpMethod.Post, Cancel, import);
            (await ListedExportsAsync(client)).Should().NotContain([stored, readWhenDownloaded, pending], who);
            (await ListedImportsAsync(client)).Should().NotContain(import, who);
        }

        const string master = "the master secret";
        (await ListedExportsAsync(world.Master)).Should().Contain([stored, readWhenDownloaded, pending]);
        (await ListedImportsAsync(world.Master)).Should().Contain(import);
        (await ExportStateAsync(world.Master, stored)).Should().Be("completed");
        (await world.Master.GetFromJsonAsync<JobProgressApiResponse>(Progress(pending), Ct))!.JobId.Should().Be(pending);

        (await DownloadedNamesAsync(master, world.Master, stored)).Should().BeEmpty(
            "the job ran as nobody, and the file is served as it was stored");
        (await DownloadedNamesAsync(master, world.Master, readWhenDownloaded)).Should().BeEmpty(
            "the table is read as the job's requester — nobody — not as the master secret downloading it");

        (await world.Master.PostAsync(Cancel(pending), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ExportStateAsync(world.Master, pending)).Should().Be("cancelled");
        (await world.Master.PostAsync(Cancel(import), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ImportJobAsync(world.Master, import)).Status.Should().Be("cancelled");
    }

    // ---- callers without an id -----------------------------------------------------------------

    /// <summary>
    /// A deployment that enforces no secrets has only anonymous callers. Within a project they are
    /// one caller — none has an id to tell it from another — and a job stays reachable to them. The
    /// project still holds: an anonymous caller of another project is answered "no such job".
    /// </summary>
    [Fact]
    public async Task Anonymous_callers_of_one_project_reach_each_others_jobs_and_those_of_another_project_do_not()
    {
        var first = _fixture.Api.Client;
        var second = _fixture.Api.CreateClientWithProject(_fixture.Api.ProjectId);
        var elsewhere = await _fixture.Api.CreateClientWithNewProjectAsync(Ct);

        var table = await CreateTableAsync(first);
        await InsertAsync(first, table, "one");
        await InsertAsync(first, table, "two");

        var job = await RequestExportAsync(first, table);
        var toCancel = await RequestExportAsync(second, table);
        var import = await RequestImportAsync(first, table, ("three", "nobody"));

        const string outsider = "an anonymous caller of another project";
        foreach (var export in new[] { job, toCancel })
        {
            await ShouldBeNoSuchJobAsync(outsider, elsewhere, HttpMethod.Get, ExportStatus, export);
            await ShouldBeNoSuchJobAsync(outsider, elsewhere, HttpMethod.Get, Progress, export);
            await ShouldBeNoSuchJobAsync(outsider, elsewhere, HttpMethod.Get, Download, export);
            await ShouldBeNoSuchJobAsync(outsider, elsewhere, HttpMethod.Post, Cancel, export);
        }

        await ShouldBeNoSuchJobAsync(outsider, elsewhere, HttpMethod.Get, ImportStatus, import);
        await ShouldBeNoSuchJobAsync(outsider, elsewhere, HttpMethod.Post, Cancel, import);
        (await ListedExportsAsync(elsewhere)).Should().BeEmpty();
        (await ListedImportsAsync(elsewhere)).Should().BeEmpty();

        await CarryOutExportsAsync(job);

        (await ExportStateAsync(second, job)).Should().Be("completed", "the other anonymous caller of the project reads its status");
        (await second.GetFromJsonAsync<JobProgressApiResponse>(Progress(job), Ct))!.JobId.Should().Be(job);
        (await ListedExportsAsync(second)).Should().Contain([job, toCancel]);
        (await DownloadedNamesAsync("the other anonymous caller", second, job)).Should().BeEquivalentTo(["one", "two"]);
        (await ImportJobAsync(second, import)).JobId.Should().Be(import);

        (await first.PostAsync(Cancel(toCancel), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the first anonymous caller cancels what the second requested");
        (await second.PostAsync(Cancel(import), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The master secret has no id either. A job it requested read the table past every policy, and
    /// a caller that does not bypass policies is not its requester for having no id too: the host
    /// that enforces nothing, reaching the same database, does not serve that file to an anonymous
    /// caller.
    /// </summary>
    [Fact]
    public async Task A_job_requested_with_the_master_secret_is_not_there_for_an_anonymous_caller()
    {
        var world = await CreateWorldAsync();
        var anonymous = _fixture.Api.Client;

        var job = await RequestExportAsync(world.Master, world.Table);
        var pending = await RequestExportAsync(world.Master, world.Table);
        await CarryOutExportsAsync(job);

        const string who = "an anonymous caller of the project";
        await ShouldBeNoSuchJobAsync(who, anonymous, HttpMethod.Get, ExportStatus, job);
        await ShouldBeNoSuchJobAsync(who, anonymous, HttpMethod.Get, Progress, job);
        await ShouldBeNoSuchJobAsync(who, anonymous, HttpMethod.Get, Download, job);
        await ShouldBeNoSuchJobAsync(who, anonymous, HttpMethod.Post, Cancel, pending);
        (await ListedExportsAsync(anonymous)).Should().NotContain([job, pending]);

        (await DownloadedNamesAsync("the master secret", world.Master, job))
            .Should().BeEquivalentTo(["a-1", "a-2", "b-1"], "requested with the master secret, the job read every row");
    }

    /// <summary>
    /// In process, the caller is the ambient security context. Where there is none, nobody is
    /// asking, and no job is there for nobody — not even one an anonymous caller requested.
    /// </summary>
    [Fact]
    public async Task Without_an_ambient_caller_the_service_finds_no_job()
    {
        var project = _fixture.Api.ProjectId;
        var table = await CreateTableAsync(_fixture.Api.Client);
        var job = await RequestExportAsync(_fixture.Api.Client, table);
        var bulk = _fixture.Api.Services.GetRequiredService<IBulkOperationService>();

        await As(null, async () =>
        {
            (await bulk.GetExportJobAsync(project, job, Ct)).Should().BeNull();
            (await bulk.GetJobProgressAsync(project, job, Ct)).Should().BeNull();
            (await bulk.ListExportJobsAsync(project, 1000, 0, Ct)).Should().BeEmpty();
            (await bulk.OpenExportFileAsync(project, job, Ct)).Should().BeNull();
            (await bulk.CancelJobAsync(project, job, Ct)).Should().BeFalse();
        });

        (await As(SecurityContext.Anonymous(project), () => bulk.GetExportJobAsync(project, job, Ct)))
            .Should().NotBeNull("an anonymous caller of the project requested it");
        (await As(SecurityContext.Anonymous(project), () => bulk.GetExportJobAsync(Guid.NewGuid(), job, Ct)))
            .Should().BeNull("the job is of another project than the one asked in");
        (await As(User(project, UserA), () => bulk.GetExportJobAsync(project, job, Ct)))
            .Should().BeNull("a caller with an id is not the anonymous caller that requested it");
        (await As(SecurityContext.Service(project), () => bulk.GetExportJobAsync(project, job, Ct)))
            .Should().NotBeNull("a caller that bypasses row-level security reaches every job of the project");
    }

    // ---- the processor is not a caller ---------------------------------------------------------

    /// <summary>
    /// The job processor has no caller, and the rule above is not for it: it finds the pending jobs
    /// of every requester and every project, and carries each out as its own requester.
    /// </summary>
    [Fact]
    public async Task The_job_processor_carries_out_the_jobs_of_every_requester_and_every_project()
    {
        var world = await CreateWorldAsync();

        var ofA = await RequestExportAsync(world.Requester, world.Table);
        var ofB = await RequestExportAsync(world.AnotherCaller, world.Table);
        var ofMaster = await RequestExportAsync(world.Master, world.Table);
        var ofAnonymous = await RequestExportAsync(_fixture.Api.Client, world.Table);
        var ofNobody = await RequestExportAsync(world.Requester, world.Table);
        await _rls.ForgetRequesterAsync(ExportJobs, ofNobody);

        var tableElsewhere = await CreateTableAsync(world.MasterElsewhere);
        await InsertAsync(world.MasterElsewhere, tableElsewhere, "elsewhere-1");
        var ofAnotherProject = await RequestExportAsync(world.MasterElsewhere, tableElsewhere);

        var import = await RequestImportAsync(
            world.Requester, world.Table, ("a-3", world.OwnerA), ("b-2", world.OwnerB));

        await CarryOutExportsAsync(ofA, ofB, ofMaster, ofAnonymous, ofNobody, ofAnotherProject);
        await CarryOutImportsAsync(import);

        async Task<List<string>> StoredNamesAsync(Guid jobId) =>
            Encoding.UTF8.GetString((await _rls.StoredExportFileAsync(jobId))!)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Skip(1)
                .ToList();

        (await StoredNamesAsync(ofA)).Should().BeEquivalentTo(["a-1", "a-2"]);
        (await StoredNamesAsync(ofB)).Should().BeEquivalentTo(["b-1"]);
        (await StoredNamesAsync(ofMaster)).Should().BeEquivalentTo(["a-1", "a-2", "b-1"]);
        (await StoredNamesAsync(ofAnonymous)).Should().BeEmpty("an anonymous caller owns no row");
        (await StoredNamesAsync(ofNobody)).Should().BeEmpty("a job with no requester runs as nobody");
        (await StoredNamesAsync(ofAnotherProject)).Should().BeEquivalentTo(["elsewhere-1"]);

        foreach (var job in new[] { ofA, ofB, ofMaster, ofAnonymous, ofNobody })
        {
            (await ExportStateAsync(world.Master, job)).Should().Be("completed");
        }

        (await ExportStateAsync(world.MasterElsewhere, ofAnotherProject)).Should().Be("completed");

        // The import wrote the row its requester may write and refused the other.
        var imported = await ImportJobAsync(world.Requester, import);
        imported.Status.Should().Be("completed");
        imported.SuccessCount.Should().Be(1);
        imported.ErrorCount.Should().Be(1);
    }
}
