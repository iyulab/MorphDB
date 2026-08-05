using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins the EDM document OData clients read before they read anything else.
/// <para>
/// <c>$metadata</c> is a published contract that never passes through a package: a client points a
/// code generator at it and gets types named after what it says. The suite asked only that the
/// response be XML with a 200, which is true of an empty document — so the names, the key and the
/// system properties could all have changed without a test noticing. The GraphQL schema had the
/// same shape of gap and it cost a silently republished type name; this is that lesson applied to
/// the surface next door.
/// </para>
/// <para>
/// Table names here end in digits and nothing else, so the expected entity-type name can be
/// written out — <c>meta_check_7</c> becomes <c>MetaCheck7</c> — instead of being derived by
/// repeating the server's own casing routine, which would agree with the server whatever the
/// server did.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataMetadataContractTests
{
    private static readonly XNamespace Edmx = "http://docs.oasis-open.org/odata/ns/edmx";
    private static readonly XNamespace Edm = "http://docs.oasis-open.org/odata/ns/edm";

    private readonly HttpClient _client;

    public ODataMetadataContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    /// <summary>Creates <c>meta_check_{digits}</c> and returns the entity type it must appear as.</summary>
    private async Task<string> CreateTableAsync()
    {
        var suffix = Math.Abs(Guid.NewGuid().GetHashCode()).ToString(CultureInfo.InvariantCulture);
        var tableName = $"meta_check_{suffix}";

        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "display_name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "age", Type = "integer", Nullable = true },
            ],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return $"MetaCheck{suffix}";
    }

    private async Task<XDocument> MetadataAsync()
    {
        var response = await _client.GetAsync("/odata/$metadata");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return XDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static XElement EntityType(XDocument metadata, string name) =>
        metadata.Descendants(Edm + "EntityType")
            .Single(e => (string?)e.Attribute("Name") == name);

    [Fact]
    public async Task A_table_appears_as_an_entity_type_and_an_entity_set_under_its_PascalCase_name()
    {
        var expected = await CreateTableAsync();

        var metadata = await MetadataAsync();

        metadata.Descendants(Edm + "EntityType")
            .Select(e => (string?)e.Attribute("Name"))
            .Should().Contain(expected);

        metadata.Descendants(Edm + "EntitySet")
            .Select(e => (string?)e.Attribute("Name"))
            .Should().Contain(expected, "an entity type nothing exposes is not addressable");
    }

    [Fact]
    public async Task Every_entity_is_keyed_by_the_same_system_property()
    {
        var expected = await CreateTableAsync();

        var entity = EntityType(await MetadataAsync(), expected);

        var key = entity.Element(Edm + "Key")?.Element(Edm + "PropertyRef");
        key.Should().NotBeNull("a keyless entity type cannot be addressed by key");
        ((string?)key!.Attribute("Name")).Should().Be("_id");

        var id = entity.Elements(Edm + "Property").Single(p => (string?)p.Attribute("Name") == "_id");
        ((string?)id.Attribute("Type")).Should().Be("Edm.Guid");
        ((string?)id.Attribute("Nullable")).Should().Be("false");
    }

    [Fact]
    public async Task The_timestamps_a_client_can_order_by_are_declared()
    {
        var expected = await CreateTableAsync();

        var properties = EntityType(await MetadataAsync(), expected)
            .Elements(Edm + "Property")
            .Select(p => (string?)p.Attribute("Name"))
            .ToList();

        properties.Should().Contain("_created_at").And.Contain("_updated_at");
    }

    [Fact]
    public async Task Declared_columns_keep_their_own_names_and_nullability()
    {
        var expected = await CreateTableAsync();

        var properties = EntityType(await MetadataAsync(), expected)
            .Elements(Edm + "Property")
            .ToDictionary(p => (string)p.Attribute("Name")!, p => p);

        // Columns are not PascalCased the way the entity type is: the type name is an OData-facing
        // identifier, while a column name is the one the caller declared and queries by everywhere
        // else. A client reading this document has to be able to write the same filter it would
        // write against the REST surface.
        properties.Should().ContainKey("display_name");
        ((string?)properties["display_name"].Attribute("Type")).Should().Be("Edm.String");
        ((string?)properties["display_name"].Attribute("Nullable")).Should().Be("false");

        // Nullable is the EDM default, so the serializer omits the attribute rather than writing
        // it — absent and "true" are the same statement to a client, and both must stay distinct
        // from the explicit "false" above.
        properties.Should().ContainKey("age");
        ((string?)properties["age"].Attribute("Type")).Should().Be("Edm.Int32");
        ((string?)properties["age"].Attribute("Nullable") ?? "true").Should().Be("true");
    }

    [Fact]
    public async Task The_project_column_is_not_declared_to_clients()
    {
        var expected = await CreateTableAsync();

        var properties = EntityType(await MetadataAsync(), expected)
            .Elements(Edm + "Property")
            .Select(p => (string?)p.Attribute("Name"));

        // Rows stopped carrying it; the EDM must not describe it either, or a generated client
        // carries a field the data never fills.
        properties.Should().NotContain("project_id");
    }

    [Fact]
    public async Task The_document_declares_the_container_clients_bind_to()
    {
        var metadata = await MetadataAsync();

        metadata.Root!.Name.Should().Be(Edmx + "Edmx");

        var container = metadata.Descendants(Edm + "EntityContainer").Single();
        ((string?)container.Attribute("Name")).Should().Be("MorphDBContainer");
        ((string?)container.Parent!.Attribute("Namespace")).Should().Be("MorphDB");
    }
}
