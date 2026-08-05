using System.Net;
using System.Text;
using System.Text.Json;
using MorphDB.Client;
using ClientModels = MorphDB.Client.Models;
using ServerModels = MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the client's project surface to the endpoints the server actually serves, the way
/// <see cref="ClientWireContractTests"/> and <see cref="ClientBatchContractTests"/> do for schema
/// and batch.
/// <para>
/// This surface exists because it is the pair of one that already shipped: every project endpoint
/// was reachable over HTTP and none of them through the client, so a caller doing anything with a
/// project had to leave the supported entry point. What makes it a pair rather than a new feature
/// is that it is one-to-one with those endpoints — which is a claim, and these tests are where it
/// is checked.
/// </para>
/// </summary>
public class ClientProjectContractTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Captures the request a client method issues, without a server.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _status;

        public CapturingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _status = status;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private const string ProjectJson = """
        {"id":"0197c0de-0000-4000-8000-000000000001","name":"Catalogue","slug":"catalogue",
         "systemSchema":"p_1_sys","dataSchema":"p_1_dat","status":"active",
         "settings":{"defaultLocale":null,"timezone":null,"enableAuditLog":true,
                     "auditLogRetentionDays":null,"defaultEnforceOnWrite":true,"metadata":null},
         "createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00"}
        """;

    private static MorphDBClient ClientOver(CapturingHandler handler)
        => new("http://morphdb.test", new MorphDBClientOptions { HttpMessageHandler = handler });

    // ---- routes: ProjectController is [Route("api/projects")] ----------------------------------

    [Fact]
    public async Task Create_targets_the_route_the_server_actually_serves()
    {
        var handler = new CapturingHandler(ProjectJson, HttpStatusCode.Created);
        var client = ClientOver(handler);

        await client.Projects.CreateAsync(new ClientModels.CreateProjectRequest { Name = "Catalogue" });

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/projects");
    }

    [Fact]
    public async Task Get_by_id_targets_the_route_the_server_actually_serves()
    {
        var handler = new CapturingHandler(ProjectJson);
        var client = ClientOver(handler);
        var id = Guid.Parse("0197c0de-0000-4000-8000-000000000001");

        await client.Projects.GetAsync(id);

        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be($"/api/projects/{id}");
    }

    [Fact]
    public async Task Get_by_slug_targets_the_route_the_server_actually_serves()
    {
        var handler = new CapturingHandler(ProjectJson);
        var client = ClientOver(handler);

        // Slugs are URL-safe by construction, but the client escapes anyway: a caller can pass any
        // string, and an unescaped one would silently address a different route.
        await client.Projects.GetBySlugAsync("cata logue");

        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/projects/slug/cata%20logue");
    }

    [Fact]
    public async Task Update_uses_PATCH_because_that_is_what_the_server_serves()
    {
        var handler = new CapturingHandler(ProjectJson);
        var client = ClientOver(handler);
        var id = Guid.NewGuid();

        await client.Projects.UpdateAsync(id, new ClientModels.UpdateProjectRequest { Name = "Renamed" });

        handler.Request!.Method.Should().Be(HttpMethod.Patch);
        handler.Request.RequestUri!.AbsolutePath.Should().Be($"/api/projects/{id}");
    }

    [Fact]
    public async Task Delete_targets_the_route_the_server_actually_serves()
    {
        var handler = new CapturingHandler("", HttpStatusCode.NoContent);
        var client = ClientOver(handler);
        var id = Guid.NewGuid();

        await client.Projects.DeleteAsync(id);

        handler.Request!.Method.Should().Be(HttpMethod.Delete);
        handler.Request.RequestUri!.AbsolutePath.Should().Be($"/api/projects/{id}");
    }

    [Fact]
    public async Task Stats_and_health_target_the_sub_routes_the_server_actually_serves()
    {
        var id = Guid.NewGuid();

        var statsHandler = new CapturingHandler($$"""
            {"projectId":"{{id}}",
             "systemSchemaStats":{"schemaName":"p_1_sys","tableCount":2,"indexCount":3,
                                  "totalSizeBytes":10,"dataSizeBytes":6,"indexSizeBytes":4,"lastModified":null},
             "dataSchemaStats":{"schemaName":"p_1_dat","tableCount":1,"indexCount":1,
                                "totalSizeBytes":5,"dataSizeBytes":4,"indexSizeBytes":1,"lastModified":null},
             "totalSizeBytes":15,"totalTableCount":3}
            """);
        await ClientOver(statsHandler).Projects.GetStatsAsync(id);
        statsHandler.Request!.RequestUri!.AbsolutePath.Should().Be($"/api/projects/{id}/stats");

        var healthHandler = new CapturingHandler($$"""
            {"projectId":"{{id}}","isHealthy":true,"issues":[],"checkedAt":"2026-01-01T00:00:00+00:00"}
            """);
        await ClientOver(healthHandler).Projects.GetHealthAsync(id);
        healthHandler.Request!.RequestUri!.AbsolutePath.Should().Be($"/api/projects/{id}/health");
    }

    [Fact]
    public async Task List_sends_the_query_parameters_the_server_binds()
    {
        var handler = new CapturingHandler("""{"data":[],"pagination":{"page":2,"pageSize":10,"totalCount":0}}""");
        var client = ClientOver(handler);

        await client.Projects.ListAsync(status: "active", page: 2, pageSize: 10);

        // ProjectQueryParameters binds status/page/pageSize from the query string.
        var query = handler.Request!.RequestUri!.Query;
        query.Should().Contain("page=2").And.Contain("pageSize=10").And.Contain("status=active");
    }

    [Fact]
    public async Task List_omits_the_status_filter_when_none_is_asked_for()
    {
        var handler = new CapturingHandler("""{"data":[],"pagination":{"page":1,"pageSize":50,"totalCount":0}}""");
        var client = ClientOver(handler);

        await client.Projects.ListAsync();

        // An empty `status=` is not the same request as no status at all: the server parses the
        // value and would filter on a status nobody asked about if it ever started accepting one.
        handler.Request!.RequestUri!.Query.Should().NotContain("status=");
    }

    // ---- wire shapes: the client types are the server types ------------------------------------

    [Fact]
    public void ServerProjectResponse_DeserializesInto_ClientProjectInfo()
    {
        var server = new ServerModels.ProjectApiResponse
        {
            Id = Guid.NewGuid(),
            Name = "Catalogue",
            Slug = "catalogue",
            SystemSchema = "p_1_sys",
            DataSchema = "p_1_dat",
            Status = "active",
            Settings = new ServerModels.ProjectSettingsApiModel
            {
                DefaultLocale = "en-GB",
                Timezone = "Europe/London",
                EnableAuditLog = false,
                AuditLogRetentionDays = 30,
                DefaultEnforceOnWrite = false,
                Metadata = new Dictionary<string, string> { ["tier"] = "gold" },
            },
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1),
        };

        var client = JsonSerializer.Deserialize<ClientModels.ProjectInfo>(
            JsonSerializer.Serialize(server, WebOptions), WebOptions);

        client.Should().NotBeNull();
        client!.Id.Should().Be(server.Id);
        client.Name.Should().Be("Catalogue");
        client.Slug.Should().Be("catalogue");
        client.SystemSchema.Should().Be("p_1_sys");
        client.DataSchema.Should().Be("p_1_dat");
        client.Status.Should().Be("active");
        client.CreatedAt.Should().Be(server.CreatedAt);
        client.UpdatedAt.Should().Be(server.UpdatedAt);
        client.Settings.Should().NotBeNull();
        client.Settings!.DefaultLocale.Should().Be("en-GB");
        client.Settings.Timezone.Should().Be("Europe/London");
        client.Settings.EnableAuditLog.Should().BeFalse();
        client.Settings.AuditLogRetentionDays.Should().Be(30);
        client.Settings.DefaultEnforceOnWrite.Should().BeFalse();
        client.Settings.Metadata.Should().ContainKey("tier");
    }

    [Fact]
    public void ClientCreateRequest_DeserializesInto_ServerRequest()
    {
        var client = new ClientModels.CreateProjectRequest
        {
            ProjectId = Guid.NewGuid(),
            Name = "Catalogue",
            Slug = "catalogue",
            Settings = new ClientModels.ProjectSettings { DefaultLocale = "en-GB" },
        };

        var server = JsonSerializer.Deserialize<ServerModels.CreateProjectApiRequest>(
            JsonSerializer.Serialize(client, WebOptions), WebOptions);

        server.Should().NotBeNull();
        server!.ProjectId.Should().Be(client.ProjectId);
        server.Name.Should().Be("Catalogue");
        server.Slug.Should().Be("catalogue");
        server.Settings!.DefaultLocale.Should().Be("en-GB");
    }

    [Fact]
    public void ClientUpdateRequest_DeserializesInto_ServerRequest()
    {
        var client = new ClientModels.UpdateProjectRequest
        {
            Name = "Renamed",
            Settings = new ClientModels.ProjectSettings { EnableAuditLog = false },
        };

        var server = JsonSerializer.Deserialize<ServerModels.UpdateProjectApiRequest>(
            JsonSerializer.Serialize(client, WebOptions), WebOptions);

        server.Should().NotBeNull();
        server!.Name.Should().Be("Renamed");
        server.Settings!.EnableAuditLog.Should().BeFalse();
    }

    [Fact]
    public void ServerStatsAndHealth_DeserializeInto_TheirClientCounterparts()
    {
        var stats = new ServerModels.ProjectStatsApiResponse
        {
            ProjectId = Guid.NewGuid(),
            SystemSchemaStats = new ServerModels.SchemaStatsApiResponse
            {
                SchemaName = "p_1_sys",
                TableCount = 2,
                IndexCount = 3,
                TotalSizeBytes = 10,
                DataSizeBytes = 6,
                IndexSizeBytes = 4,
                LastModified = DateTimeOffset.UnixEpoch,
            },
            DataSchemaStats = new ServerModels.SchemaStatsApiResponse
            {
                SchemaName = "p_1_dat",
                TableCount = 1,
                IndexCount = 1,
                TotalSizeBytes = 5,
                DataSizeBytes = 4,
                IndexSizeBytes = 1,
                LastModified = null,
            },
            TotalSizeBytes = 15,
            TotalTableCount = 3,
        };

        var clientStats = JsonSerializer.Deserialize<ClientModels.ProjectStats>(
            JsonSerializer.Serialize(stats, WebOptions), WebOptions);

        clientStats.Should().NotBeNull();
        clientStats!.ProjectId.Should().Be(stats.ProjectId);
        clientStats.SystemSchemaStats.SchemaName.Should().Be("p_1_sys");
        clientStats.SystemSchemaStats.LastModified.Should().Be(DateTimeOffset.UnixEpoch);
        clientStats.DataSchemaStats.IndexSizeBytes.Should().Be(1);
        clientStats.TotalSizeBytes.Should().Be(15);
        clientStats.TotalTableCount.Should().Be(3);

        var health = new ServerModels.SchemaHealthApiResponse
        {
            ProjectId = stats.ProjectId,
            IsHealthy = false,
            Issues =
            [
                new ServerModels.SchemaHealthIssueApiResponse
                {
                    Code = "MISSING_TABLE",
                    Message = "A table in the metadata has no physical counterpart.",
                    Severity = "error",
                    AffectedObject = "products",
                },
            ],
            CheckedAt = DateTimeOffset.UnixEpoch,
        };

        var clientHealth = JsonSerializer.Deserialize<ClientModels.SchemaHealthReport>(
            JsonSerializer.Serialize(health, WebOptions), WebOptions);

        clientHealth.Should().NotBeNull();
        clientHealth!.IsHealthy.Should().BeFalse();
        clientHealth.Issues.Should().ContainSingle();
        clientHealth.Issues[0].Code.Should().Be("MISSING_TABLE");
        clientHealth.Issues[0].Severity.Should().Be("error");
        clientHealth.Issues[0].AffectedObject.Should().Be("products");
    }

    // ---- the hazard the surface must not hide --------------------------------------------------

    [Fact]
    public async Task Updating_settings_sends_every_field_because_the_server_stores_by_replacement()
    {
        var handler = new CapturingHandler(ProjectJson);
        var client = ClientOver(handler);

        // A caller who wants only to disable audit logging still sends the whole object, because
        // that is what the server stores. The client's defaults are the server's defaults, so what
        // is built here and what ends up stored are the same thing rather than two things that
        // agree until one of them changes.
        await client.Projects.UpdateAsync(
            Guid.NewGuid(),
            new ClientModels.UpdateProjectRequest
            {
                Settings = new ClientModels.ProjectSettings { EnableAuditLog = false },
            });

        var sent = JsonDocument.Parse(handler.Body!).RootElement.GetProperty("settings");
        sent.GetProperty("enableAuditLog").GetBoolean().Should().BeFalse();

        // The field a partial body loses most expensively. It is present and true here, which is
        // what the caller asked for by not saying otherwise -- the point is that it is *stated*,
        // not that the server filled it in on the way past.
        sent.GetProperty("defaultEnforceOnWrite").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ClientSettings_default_to_the_same_values_the_server_does()
    {
        // If these ever drift, a caller building a settings object gets a project configured
        // differently from the one they read, and nothing in either type says so.
        var client = new ClientModels.ProjectSettings();
        var server = new ServerModels.ProjectSettingsApiModel();

        client.EnableAuditLog.Should().Be(server.EnableAuditLog);
        client.DefaultEnforceOnWrite.Should().Be(server.DefaultEnforceOnWrite);
        client.AuditLogRetentionDays.Should().Be(server.AuditLogRetentionDays);
        client.DefaultLocale.Should().Be(server.DefaultLocale);
        client.Timezone.Should().Be(server.Timezone);
    }
}
