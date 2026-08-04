using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-doc half of the constraint-boundary gate. The 2026-07-22 amendment moved every
/// integrity constraint except CHECK to physical enforcement, and for thirteen days the two
/// documents a reader meets first — the README principles table and PHILOSOPHY — kept describing
/// the superseded boundary while ARCHITECTURE and API carried the current one. Nothing failed,
/// because no gate had the constraint boundary in range: the existing doc gates cover routes and
/// error codes. The stale text was then read back as the norm and produced a proposal to invert a
/// default that was correct all along, which is the cost this file exists to prevent — a document
/// that outlives its code does not merely mislead, it generates work.
///
/// ARCHITECTURE.md is the comparison target here only because it is machine-comparable; it is
/// itself pinned to observed database behaviour by ConstraintBoundaryContractTests, so no
/// hand-written inventory of the boundary exists anywhere. A second hand-written list would
/// reproduce exactly the failure this gate is for.
/// </summary>
public class ConstraintBoundaryDocParityTests
{
    /// <summary>
    /// Phrasings that asserted the superseded boundary. They are retired rather than merely
    /// corrected: prose cannot be table-compared, so the only durable check on a sentence is that
    /// it does not come back. Taken from the sites that actually carried them — including the
    /// README, which is where the claim did its damage and which no table gate can reach.
    /// </summary>
    private static readonly IReadOnlyList<string> RetiredPhrasings =
    [
        "only PK/Index physical",
        "recommends NOT creating physical",
        "Virtual FK is recommended",
        "Virtual Constraint philosophy",
    ];

    private static readonly IReadOnlyList<string> WatchedFiles =
    [
        "README.md",
        "docs/PHILOSOPHY.md",
        "docs/ARCHITECTURE.md",
        "docs/CONSTITUTION.md",
        "src/MorphDB.Npgsql/Services/PostgresSchemaManager.cs",
        "src/MorphDB.Npgsql/Pipeline/Validators/ForeignKeyValidator.cs",
    ];

    [Fact]
    public void The_philosophy_table_agrees_with_the_architecture_table()
    {
        var architecture = ConstraintBoundaryDoc.Boundary(
            ConstraintBoundaryDoc.ReadRepoFile("docs/ARCHITECTURE.md"));
        var philosophy = ConstraintBoundaryDoc.Boundary(
            ConstraintBoundaryDoc.ReadRepoFile("docs/PHILOSOPHY.md"));

        architecture.Should().NotBeEmpty("the Physical vs Virtual table must exist in ARCHITECTURE.md");
        philosophy.Should().NotBeEmpty("the constraint boundary table must exist in PHILOSOPHY.md");

        philosophy.Should().BeEquivalentTo(architecture,
            "the same boundary stated twice must be stated the same way — a reader who opens the " +
            "philosophy document is not told to go verify it against the architecture one. Only the " +
            "physical/virtual assignment is compared; the rationale prose is each document's own.");
    }

    [Fact]
    public void Retired_phrasings_do_not_come_back()
    {
        foreach (var file in WatchedFiles)
        {
            var text = ConstraintBoundaryDoc.ReadRepoFile(file);
            foreach (var phrasing in RetiredPhrasings)
            {
                text.Should().NotContainEquivalentOf(phrasing,
                    $"'{phrasing}' states the superseded boundary, and {file} is a place a reader " +
                    "meets before the constitution");
            }
        }
    }
}
