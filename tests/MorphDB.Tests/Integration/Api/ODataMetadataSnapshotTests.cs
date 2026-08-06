using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Holds the shape one table takes in the EDM document, for a table that declares a column of every
/// type the store can give a column.
/// <para>
/// The other <c>$metadata</c> gate asks for the names it was told to watch — an entity type, a key,
/// two timestamps, one column of each of two types. That leaves the mapping from a declared type to
/// an EDM type mostly unwatched, and it is the part of this document a consumer's code generator
/// turns into field types: a type that starts arriving as <c>Edm.String</c> where it used to be
/// <c>Edm.Decimal</c> changes what their program does with the value, while every named fact stays
/// true. Naming what to watch only catches what has already gone wrong once.
/// </para>
/// <para>
/// A whole-document snapshot is not available here and asking for one would be a mistake worth
/// naming: unlike a schema built from fixed types, this document is built from whatever tables
/// exist, so its contents are the suite's other tests. What does not vary is how one table becomes
/// one entity type, and that is what is recorded.
/// </para>
/// <para>
/// <b>When this fails:</b> read the diff and decide. An intended change is recorded in the release
/// notes and the file is updated in the same commit; an unintended one is the finding. Updating the
/// file to make the build green, without reading what moved, is the one use that defeats it — the
/// file is the contract, not a cache of the last run.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataMetadataSnapshotTests
{
    private const string SnapshotPath = "tests/MorphDB.Tests/Contracts/odata-entity-type.xml";

    /// <summary>The entity type's own name is the table's, so it is held apart from its shape.</summary>
    private const string Placeholder = "TypeCoverage";

    private static readonly XNamespace Edm = "http://docs.oasis-open.org/odata/ns/edm";

    /// <summary>
    /// The types no column can be declared with, because the store has no native type for them. The
    /// second fact below asserts that of the store rather than trusting this list: a type that gains
    /// storage stops belonging here, and the gate that would then be silently narrower says so.
    /// </summary>
    private static readonly MorphDataType[] WithoutNativeType =
    [
        MorphDataType.Lookup,
        MorphDataType.Computed,
    ];

    private static MorphDataType[] Declarable =>
        Enum.GetValues<MorphDataType>().Except(WithoutNativeType).ToArray();

    private readonly HttpClient _client;

    public ODataMetadataSnapshotTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task One_table_becomes_the_entity_type_recorded_as_published()
    {
        var entityTypeName = await CreateCoveringTableAsync();

        var metadata = XDocument.Parse(await _client.GetStringAsync("/odata/$metadata"));

        var entity = metadata.Descendants(Edm + "EntityType")
            .Single(e => (string?)e.Attribute("Name") == entityTypeName);
        entity.SetAttributeValue("Name", Placeholder);

        Normalize(entity.ToString()).Should().Be(
            Normalize(ConstraintBoundaryDoc.ReadRepoFile(SnapshotPath)),
            "the EDM document is a published contract a client generates types from; a difference "
            + "here is either a release note or a defect, and both are decided by reading the diff");
    }

    /// <summary>
    /// Every declared type reaches the snapshot, or the store refuses it. Without this, adding a
    /// type would leave the snapshot silently one column short of the surface it claims to hold —
    /// and a new type's mapping is exactly the thing no one has read yet.
    /// </summary>
    [Fact]
    public void Every_type_is_either_in_the_snapshot_or_one_the_store_cannot_give_a_column()
    {
        foreach (var type in Declarable)
        {
            TypeMapper.ToNativeType(type).Should().NotBeNullOrEmpty();
        }

        foreach (var type in WithoutNativeType)
        {
            var refused = () => TypeMapper.ToNativeType(type);
            refused.Should().Throw<ArgumentOutOfRangeException>(
                "a type left out of the snapshot is left out because no column can be declared "
                + "with it, and that has to stay true for the omission to be honest");
        }
    }

    /// <summary>
    /// Creates a table carrying one column per declarable type and returns the entity type name it
    /// must appear under. Column names are the type names, so the snapshot reads as the mapping it
    /// is holding rather than as a table someone invented.
    /// </summary>
    private async Task<string> CreateCoveringTableAsync()
    {
        var suffix = Math.Abs(Guid.NewGuid().GetHashCode()).ToString(CultureInfo.InvariantCulture);

        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = $"odata_snapshot_{suffix}",
            Columns = [.. Declarable.Select(type => new CreateColumnApiRequest
            {
                Name = $"c_{type.ToString().ToLowerInvariant()}",
                Type = type.ToString(),
                // One column declares itself required, because a present "false" and an absent
                // attribute are the two statements this document makes about nullability and both
                // have to be in the record.
                Nullable = type != MorphDataType.Text,
            })],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return $"OdataSnapshot{suffix}";
    }

    private static string Normalize(string xml) =>
        xml.Replace("\r\n", "\n").TrimEnd() + "\n";
}
