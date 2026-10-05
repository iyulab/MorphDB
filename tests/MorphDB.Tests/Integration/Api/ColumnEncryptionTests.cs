using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MorphDB.Core.Encryption;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The one encryption configuration the service accepts: a master key with
/// <c>EncryptAllByDefault</c> off. It encrypts nothing — no request can mark a column encrypted — so
/// rows round-trip in clear and the encryption routes answer instead of <c>503</c>, reporting no
/// encrypted value. A configuration that would encrypt is refused at startup
/// (<see cref="Unit.EncryptionConfigurationTests"/>).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class ColumnEncryptionTests
{
    private readonly ApiIntegrationFixture _fixture;

    public ColumnEncryptionTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_key_that_encrypts_nothing_leaves_rows_in_clear_and_the_routes_report_no_encrypted_value()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = _fixture.Api.WithEncryption(new DataEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            EncryptAllByDefault = false,
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", _fixture.Api.ProjectId.ToString());

        var table = $"enc_off_{Guid.NewGuid():N}"[..28];
        (await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "note", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "amount", Type = "integer", Nullable = true },
            ],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["note"] = "hello", ["amount"] = 42 }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        using var rows = JsonDocument.Parse(await client.GetStringAsync($"/api/data/{table}?filter=note:eq:hello", ct));
        var data = rows.RootElement.GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("data").GetProperty("note").GetString().Should().Be("hello");
        data[0].GetProperty("data").GetProperty("amount").GetInt32().Should().Be(42);

        var validate = await client.GetAsync($"/api/security/encryption/validate/{table}", ct);
        validate.StatusCode.Should().Be(HttpStatusCode.OK);
        using var report = JsonDocument.Parse(await validate.Content.ReadAsStringAsync(ct));
        report.RootElement.GetProperty("totalEncryptedValues").GetInt64().Should().Be(0);
    }
}
