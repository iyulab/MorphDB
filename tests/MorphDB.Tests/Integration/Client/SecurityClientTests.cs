using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's security calls, and the secret it presents, over the real service with a master
/// secret injected.
/// <para>
/// Regression. The client had no way to present a secret: its options carried a project and a
/// timeout, so against a service that enforces secrets every call from a client built on a base URL
/// was refused, the real-time connection included. And it had no method for policies or secrets, so
/// provisioning a project's access had to be written by hand against the routes.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class SecurityClientTests(ApiIntegrationFixture fixture)
{
    // The same value as the enforcement tests, so both share the one enforced host the fixture keeps per secret.
    private const string MasterSecret = "mdb_test_master_secret_value";

    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MorphDBClient EnforcedClient(string? secret, Guid? projectId = null)
    {
        var enforced = fixture.Api.WithMasterSecret(MasterSecret);
        return new MorphDBClient(enforced.Server.BaseAddress.ToString(), new MorphDBClientOptions
        {
            ProjectId = projectId ?? fixture.Api.ProjectId,
            Secret = secret,
            HttpMessageHandler = enforced.Server.CreateHandler(),
        });
    }

    private static string Name(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task A_client_without_a_secret_is_refused_and_one_with_the_master_secret_is_served()
    {
        await using var anonymous = EnforcedClient(secret: null);
        await Assert.ThrowsAsync<MorphDBAuthenticationException>(() => anonymous.Schema.GetTablesAsync(Ct));

        await using var master = EnforcedClient(MasterSecret);
        var tables = await master.Schema.GetTablesAsync(Ct);
        Assert.NotNull(tables);
    }

    [Fact]
    public async Task A_secret_is_issued_listed_used_and_revoked()
    {
        await using var master = EnforcedClient(MasterSecret);

        var issued = await master.Security.IssueSecretAsync(new IssueSecretRequest
        {
            Name = Name("reader"),
            Role = "reader",
            ProjectId = fixture.Api.ProjectId,
        }, Ct);
        Assert.False(string.IsNullOrEmpty(issued.Secret));
        Assert.Equal("reader", issued.Role);
        Assert.Equal(fixture.Api.ProjectId, issued.ProjectId);

        var listed = await master.Security.GetSecretsAsync(Ct);
        var row = Assert.Single(listed, s => s.SecretId == issued.SecretId);
        Assert.True(row.IsActive);

        // The issued secret works on the project's routes, and cannot manage secrets.
        await using (var reader = EnforcedClient(issued.Secret))
        {
            Assert.NotNull(await reader.Schema.GetTablesAsync(Ct));
            await Assert.ThrowsAsync<MorphDBAuthorizationException>(() => reader.Security.GetSecretsAsync(Ct));
        }

        Assert.True(await master.Security.RevokeSecretAsync(issued.SecretId, Ct));
        Assert.False(await master.Security.RevokeSecretAsync(Guid.NewGuid(), Ct), "no such secret is a false, not a throw");

        var afterRevoke = await master.Security.GetSecretsAsync(Ct);
        Assert.False(Assert.Single(afterRevoke, s => s.SecretId == issued.SecretId).IsActive);

        await using var revoked = EnforcedClient(issued.Secret);
        await Assert.ThrowsAsync<MorphDBAuthenticationException>(() => revoked.Schema.GetTablesAsync(Ct));
    }

    [Fact]
    public async Task A_policy_is_created_read_changed_and_deleted_on_its_table()
    {
        await using var master = EnforcedClient(MasterSecret);
        var table = await master.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("pol"),
            Columns = [new CreateColumnRequest { Name = "owner", Type = "text" }],
        }, Ct);

        var created = await master.Security.CreatePolicyAsync(new CreateSecurityPolicyRequest
        {
            Name = "own_rows",
            TableName = table.Name,
            PolicyType = PolicyType.Select,
            Expression = "owner = {{user_id}}",
            Description = "Rows of the caller",
        }, Ct);
        Assert.Equal(PolicyType.Select, created.PolicyType);
        Assert.Equal(table.Id, created.TableId);
        Assert.True(created.IsActive);

        var policies = await master.Security.GetPoliciesAsync(table.Name, Ct);
        Assert.Equal(created.Id, Assert.Single(policies).Id);

        var changed = await master.Security.UpdatePolicyAsync(created.Id, new UpdateSecurityPolicyRequest { IsActive = false }, Ct);
        Assert.False(changed.IsActive);
        Assert.Equal("owner = {{user_id}}", changed.Expression);

        await master.Security.DeletePolicyAsync(created.Id, Ct);
        Assert.Empty(await master.Security.GetPoliciesAsync(table.Name, Ct));
    }

    /// <summary>
    /// The real-time connection presents the same secret, and the project set after construction.
    /// It used to send neither: no secret, so an enforcing hub refused the connection, and the project
    /// the options held at construction, so <see cref="MorphDBClient.SetProjectId"/> reached every REST
    /// call and not the hub.
    /// </summary>
    [Fact]
    public async Task The_realtime_connection_presents_the_secret_and_the_project_set_after_construction()
    {
        await using var client = EnforcedClient(MasterSecret, projectId: Guid.Empty);
        client.SetProjectId(fixture.Api.ProjectId);

        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("rts"),
            Columns = [new CreateColumnRequest { Name = "name", Type = "text" }],
        }, Ct);

        var received = new Queue<ChangeNotification>();
        await using var subscription = await client.Realtime.SubscribeAsync(table.Name, change =>
        {
            lock (received)
                received.Enqueue(change);
        }, Ct);

        var created = await client.Data.InsertAsync(table.Name, new Dictionary<string, object?> { ["name"] = "x" }, Ct);

        var deadline = DateTimeOffset.UtcNow + NotificationTimeout;
        ChangeNotification? change = null;
        while (change is null && DateTimeOffset.UtcNow < deadline)
        {
            lock (received)
                received.TryDequeue(out change);
            if (change is null)
                await Task.Delay(100, Ct);
        }

        Assert.NotNull(change);
        Assert.Equal(created.Id, change.RecordId);
        Assert.Equal(fixture.Api.ProjectId, change.ProjectId);
    }
}
