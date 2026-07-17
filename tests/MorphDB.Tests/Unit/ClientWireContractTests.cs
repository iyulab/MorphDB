using System.Text.Json;
using ClientModels = MorphDB.Client.Models;
using ServerModels = MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Guards the MorphDB.Client schema models against the server wire contract
/// (issue rest-jsonelement-defects, defect #4). The client was out of sync with the server
/// (dataType/nativeType/physicalName/isNullable vs type/nullable/unique/primaryKey/indexed),
/// causing CreateTableAsync/GetTableAsync to throw on deserialization. These tests round-trip
/// through JsonSerializerDefaults.Web — the exact options HttpClient's JSON extensions use.
/// </summary>
public class ClientWireContractTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ServerColumnResponse_DeserializesInto_ClientColumnInfo()
    {
        // Arrange — a column exactly as the server serializes it on the wire
        var server = new ServerModels.ColumnApiResponse
        {
            Id = Guid.NewGuid(),
            Name = "sku",
            Type = "text",
            Nullable = false,
            Unique = true,
            PrimaryKey = false,
            Indexed = true,
            Default = null,
            Check = "sku <> ''",
            Position = 3,
            IsDerived = false
        };
        var wire = JsonSerializer.Serialize(server, WebOptions);

        // Act — the client must deserialize it without throwing (required members present)
        var client = JsonSerializer.Deserialize<ClientModels.ColumnInfo>(wire, WebOptions);

        // Assert — every field maps
        client.Should().NotBeNull();
        client!.Id.Should().Be(server.Id);
        client.Name.Should().Be("sku");
        client.Type.Should().Be("text");
        client.Nullable.Should().BeFalse();
        client.Unique.Should().BeTrue();
        client.PrimaryKey.Should().BeFalse();
        client.Indexed.Should().BeTrue();
        client.Check.Should().Be("sku <> ''");
        client.Position.Should().Be(3);
        client.IsDerived.Should().BeFalse();
    }

    [Fact]
    public void ServerTableResponse_DeserializesInto_ClientTableInfo()
    {
        // Arrange
        var server = new ServerModels.TableApiResponse
        {
            Id = Guid.NewGuid(),
            Name = "products",
            Version = 2,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Columns =
            [
                new ServerModels.ColumnApiResponse { Id = Guid.NewGuid(), Name = "id", Type = "uuid", PrimaryKey = true, Position = 1 },
                new ServerModels.ColumnApiResponse { Id = Guid.NewGuid(), Name = "name", Type = "text", Nullable = false, Position = 2 }
            ]
        };
        var wire = JsonSerializer.Serialize(server, WebOptions);

        // Act
        var client = JsonSerializer.Deserialize<ClientModels.TableInfo>(wire, WebOptions);

        // Assert
        client.Should().NotBeNull();
        client!.Id.Should().Be(server.Id);
        client.Name.Should().Be("products");
        client.Version.Should().Be(2);
        client.Columns.Should().HaveCount(2);
        client.Columns[0].Name.Should().Be("id");
        client.Columns[0].PrimaryKey.Should().BeTrue();
        client.Columns[1].Type.Should().Be("text");
        client.Columns[1].Nullable.Should().BeFalse();
    }

    [Fact]
    public void ClientCreateColumnRequest_DeserializesInto_ServerRequest()
    {
        // Arrange — what the client sends
        var client = new ClientModels.CreateColumnRequest
        {
            Name = "sku",
            Type = "text",
            Nullable = false,
            Unique = true,
            Indexed = true,
            Default = "''",
            Check = "sku <> ''"
        };
        var wire = JsonSerializer.Serialize(client, WebOptions);

        // Act — the server binds it
        var server = JsonSerializer.Deserialize<ServerModels.CreateColumnApiRequest>(wire, WebOptions);

        // Assert — the constraint-bearing fields survive the wire (previously lost to name mismatch)
        server.Should().NotBeNull();
        server!.Name.Should().Be("sku");
        server.Type.Should().Be("text");
        server.Nullable.Should().BeFalse();
        server.Unique.Should().BeTrue();
        server.Indexed.Should().BeTrue();
        server.Default.Should().Be("''");
        server.Check.Should().Be("sku <> ''");
    }

    [Fact]
    public void ClientCreateTableRequest_DeserializesInto_ServerRequest()
    {
        // Arrange
        var client = new ClientModels.CreateTableRequest
        {
            Name = "products",
            Columns =
            [
                new ClientModels.CreateColumnRequest { Name = "name", Type = "text", Nullable = false }
            ],
            SystemColumns = new ClientModels.SystemColumnOptions { SoftDelete = true, RowState = true }
        };
        var wire = JsonSerializer.Serialize(client, WebOptions);

        // Act
        var server = JsonSerializer.Deserialize<ServerModels.CreateTableApiRequest>(wire, WebOptions);

        // Assert
        server.Should().NotBeNull();
        server!.Name.Should().Be("products");
        server.Columns.Should().HaveCount(1);
        server.Columns[0].Name.Should().Be("name");
        server.Columns[0].Nullable.Should().BeFalse();
        server.SystemColumns.Should().NotBeNull();
        server.SystemColumns!.SoftDelete.Should().BeTrue();
        server.SystemColumns.RowState.Should().BeTrue();
    }

    [Fact]
    public void ClientAlterColumnRequest_DeserializesInto_ServerUpdateRequest()
    {
        // Arrange
        var client = new ClientModels.AlterColumnRequest
        {
            Type = "bigint",
            Nullable = true,
            Version = 5,
            ForceCast = true
        };
        var wire = JsonSerializer.Serialize(client, WebOptions);

        // Act
        var server = JsonSerializer.Deserialize<ServerModels.UpdateColumnApiRequest>(wire, WebOptions);

        // Assert
        server.Should().NotBeNull();
        server!.Type.Should().Be("bigint");
        server.Nullable.Should().BeTrue();
        server.Version.Should().Be(5);
        server.ForceCast.Should().BeTrue();
    }
}
