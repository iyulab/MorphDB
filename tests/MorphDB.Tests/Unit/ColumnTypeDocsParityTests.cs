using System.Text.RegularExpressions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for the column type catalog (<c>docs/API.md</c> · Column types). The
/// catalog was written once by hand in an architecture document, named eight of the accepted types,
/// and stayed that way while the vocabulary grew to twenty-seven; when it was rewritten it still
/// called two virtual types stored columns. This holds every cell of the table to the code that
/// decides it: the set of names to the enum and its storage mapping, each alias to the parser, each
/// storage column to <see cref="TypeMapper"/>, and the two refused names to the members no column
/// can be created with. <see cref="DataTypeVocabularyTests"/> is the other axis — what the server
/// tells a caller at runtime — and does not read the document.
/// </summary>
public partial class ColumnTypeDocsParityTests
{
    private sealed record CatalogRow(string Type, IReadOnlyList<string> Aliases, string StoredAs, string Notes);

    /// <summary>
    /// The column types that exist as declarations but are created without a storage column of their
    /// own: the schema manager treats a declaration carrying a lookup, rollup or formula object as
    /// virtual, and the catalog documents the two of those that are also creatable by name.
    /// </summary>
    private static readonly IReadOnlySet<MorphDataType> Virtual = new HashSet<MorphDataType>
    {
        MorphDataType.Rollup,
        MorphDataType.Formula,
    };

    [Fact]
    public void The_catalog_names_exactly_the_types_a_column_can_be_created_with()
    {
        var documented = Rows().Select(r => r.Type).ToList();
        var creatable = Enum.GetValues<MorphDataType>()
            .Where(HasStorage)
            .Select(t => t.ToString().ToLowerInvariant())
            .ToList();

        documented.Should().OnlyHaveUniqueItems();
        documented.Should().BeEquivalentTo(creatable,
            "a type a column can be created with and the catalog does not name is invisible to a consumer, " +
            "and a name the catalog lists that the server refuses sends them down a path that cannot end");
    }

    [Fact]
    public void Every_name_and_alias_parses_to_the_type_of_its_row()
    {
        foreach (var row in Rows())
        {
            var expected = Enum.Parse<MorphDataType>(row.Type, ignoreCase: true);
            ApiModelExtensions.ParseDataType(row.Type).Should().Be(expected, $"`{row.Type}` is the canonical name");
            foreach (var alias in row.Aliases)
            {
                ApiModelExtensions.ParseDataType(alias).Should().Be(expected,
                    $"the catalog says `{alias}` is accepted for `{row.Type}`");
            }
        }
    }

    [Fact]
    public void Every_storage_column_is_the_one_the_type_mapper_creates()
    {
        foreach (var row in Rows())
        {
            var type = Enum.Parse<MorphDataType>(row.Type, ignoreCase: true);
            if (Virtual.Contains(type))
            {
                row.StoredAs.Should().Be("—",
                    $"`{row.Type}` is created as a virtual column; the catalog must not promise storage for it");
                continue;
            }

            row.StoredAs.Should().Be(TypeMapper.ToNativeType(type),
                $"the catalog's storage column for `{row.Type}` must be the one the type mapper actually creates");
        }
    }

    [Fact]
    public void The_refused_names_are_exactly_the_declared_members_without_storage()
    {
        var refusedInDocs = RefusedNames();
        var refusedInCode = Enum.GetValues<MorphDataType>()
            .Where(t => !HasStorage(t))
            .Select(t => t.ToString().ToLowerInvariant());

        refusedInDocs.Should().BeEquivalentTo(refusedInCode,
            "the catalog's paragraph on refused names must name the members the store has no type for, no more and no fewer");
    }

    private static bool HasStorage(MorphDataType type)
    {
        try
        {
            TypeMapper.ToNativeType(type);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string CatalogSection()
    {
        var api = DocsFiles.ReadApiReference();
        var start = api.IndexOf("### Column types", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "docs/API.md must carry the Column types section");
        var end = api.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        return end < 0 ? api[start..] : api[start..end];
    }

    private static IReadOnlyList<CatalogRow> Rows()
    {
        var rows = TableRow().Matches(CatalogSection())
            // The header row matches the shape too; a data row's storage cell is a code span or the dash.
            .Where(m => m.Groups["stored"].Value.Trim() is var cell && (cell.StartsWith('`') || cell.StartsWith('—')))
            .Select(m => new CatalogRow(
                m.Groups["type"].Value,
                Code().Matches(m.Groups["aliases"].Value).Select(a => a.Groups["v"].Value).ToList(),
                m.Groups["stored"].Value.Trim() is var stored && stored.StartsWith('`')
                    ? Code().Match(stored).Groups["v"].Value
                    : stored.Split(' ')[0],
                m.Groups["notes"].Value.Trim()))
            .ToList();

        rows.Should().NotBeEmpty("the Column types table must have rows");
        return rows;
    }

    /// <summary>
    /// Reads the paragraph that begins at <c>**refused**</c>. Every lower-case code span in it is
    /// taken as a refused name — so that paragraph is a contract for the document's author as much as
    /// for the code: a type mentioned there for contrast (<c>rollup</c>, say) must be written in
    /// prose, not in a code span, or this gate reads it as refused and fails.
    /// </summary>
    private static IReadOnlyList<string> RefusedNames()
    {
        var section = CatalogSection();
        var paragraph = section[section.IndexOf("**refused**", StringComparison.Ordinal)..];
        return Code().Matches(paragraph)
            .Select(m => m.Groups["v"].Value)
            .Where(v => v.All(char.IsLower))
            .Distinct()
            .ToList();
    }

    // | `type` | `alias`, `alias` | `stored` or — (virtual) | notes |
    [GeneratedRegex(@"^\|\s*`(?<type>[a-z]+)`\s*\|(?<aliases>[^|]*)\|(?<stored>[^|]*)\|(?<notes>[^|]*)\|\s*$", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"`(?<v>[^`]+)`")]
    private static partial Regex Code();
}
