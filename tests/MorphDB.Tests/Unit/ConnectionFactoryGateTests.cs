using System.Text.RegularExpressions;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// A security context is written to a connection's session by <see cref="MorphConnectionFactory"/>,
/// and only by it. A connection opened any other way — straight from the data source, from a data
/// source built on the side, from a bare connection string — reaches the database with whatever its
/// session happened to hold, and nothing about the statement run on it says so: it compiles, it
/// returns rows, and a policy reading the session sees nobody or, worse, sees the wrong somebody.
/// The compiler cannot tell the two kinds of open apart, so this gate reads the sources: outside the
/// few files that are the mechanism itself, nothing in the two server projects may open a connection
/// or run a command except through the factory.
/// </summary>
public partial class ConnectionFactoryGateTests
{
    private const string FactoryFile = "MorphDB.Npgsql/Infrastructure/MorphConnectionFactory.cs";

    private static readonly string[] ScannedProjects = ["MorphDB.Npgsql", "MorphDB.Service"];

    /// <summary>The files that are the mechanism rather than users of it, and why each is here.</summary>
    private static readonly string[] AllowedFiles =
    [
        // Opens the connection it then writes the context to.
        FactoryFile,

        // Builds the one data source the factory opens from.
        "MorphDB.Npgsql/ServiceCollectionExtensions.cs",

        // Holds a single dedicated LISTEN connection for the life of the process; no row is read on it.
        "MorphDB.Service/Realtime/PostgresChangeListener.cs",
    ];

    [Fact]
    public void Every_connection_is_opened_through_the_factory()
    {
        var sources = Sources();

        sources.Keys.Should().Contain(AllowedFiles, "an allowance for a file that is not there is a stale exception");

        var offences = sources
            .Where(source => !AllowedFiles.Contains(source.Key, StringComparer.Ordinal))
            .SelectMany(source => Offences(source.Key, source.Value))
            .ToList();

        offences.Should().BeEmpty(
            $"a connection opened around {nameof(MorphConnectionFactory)} has no security context written to "
            + $"its session — open it through {nameof(MorphConnectionFactory)}.{nameof(MorphConnectionFactory.OpenAsync)} "
            + $"(a caller's rows) or {nameof(MorphConnectionFactory)}.{nameof(MorphConnectionFactory.OpenSystemAsync)} "
            + "(bookkeeping) instead of from a data source or a connection string");
    }

    [Fact]
    public void Every_allowed_file_still_needs_its_allowance()
    {
        var sources = Sources();

        foreach (var file in AllowedFiles)
        {
            Offences(file, sources[file]).Should().NotBeEmpty(
                $"{file} is exempt from the gate because it opens or builds on its own; once it no longer "
                + "does, the exemption only hides whatever is added to it next");
        }
    }

    [Fact]
    public void The_gate_reports_each_way_of_opening_around_the_factory()
    {
        string[] offending =
        [
            "await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);",
            "using var connection = dataSource.OpenConnection();",
            "var connection = _dataSource.CreateConnection();",
            "await using var connection = new NpgsqlConnection(connectionString);",
            "await using var dataSource = NpgsqlDataSource.Create(connectionString);",
            "var builder = new NpgsqlDataSourceBuilder(connectionString);",
            "await using var command = dataSource.CreateCommand(\"SELECT 1\");",
            "await using var batch = _dataSource.CreateBatch();",
        ];

        foreach (var line in offending)
        {
            Offences("Sample.cs", line).Should().ContainSingle(
                $"`{line}` reaches the database without the factory, and a gate that reads past it is decoration")
                .Which.Should().StartWith("src/Sample.cs(1): ");
        }
    }

    [Fact]
    public void The_gate_does_not_report_what_goes_through_the_factory()
    {
        const string throughTheFactory = """
            await using var connection = await _connections.OpenAsync(cancellationToken);
            await using var system = await _connections.OpenSystemAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            await using var batch = connection.CreateBatch();
            ConnectionString = SanitizeConnectionString(_dataSource.ConnectionString),
            """;

        Offences("Sample.cs", throughTheFactory).Should().BeEmpty(
            "a command made from a connection the factory opened is the ordinary case — reporting it would "
            + "turn the gate red on every file and teach readers to ignore it");
    }

    /// <summary>Every source file of the scanned projects, keyed by its path under <c>src/</c>.</summary>
    private static Dictionary<string, string> Sources()
    {
        var factoryPath = ConstraintBoundaryDoc.RepoFilePath($"src/{FactoryFile}");
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(factoryPath)!, "..", ".."));

        return ScannedProjects
            .Select(project => Path.Combine(sourceRoot, project))
            .SelectMany(projectDirectory => Directory
                .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path, projectDirectory)))
            .ToDictionary(
                path => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    // The build drops generated sources under bin/ and obj/; they are not what a contributor edits,
    // and a stale copy there would be reported against a file that no longer says it.
    private static bool IsBuildOutput(string path, string root) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "bin" or "obj");

    private static IEnumerable<string> Offences(string file, string source) =>
        source.Split('\n')
            .Select((line, index) => (line, number: index + 1))
            .Where(entry => OpensAroundTheFactory().IsMatch(entry.line))
            .Select(entry => $"src/{file}({entry.number}): {entry.line.Trim()}");

    // Opening from a data source, building a connection or a data source by hand, and the data
    // source's own command and batch — which run on a pooled connection without ever showing one.
    // `CreateCommand` and `CreateBatch` are only reported on something named as a data source: on a
    // connection they are how every statement is made.
    [GeneratedRegex(
        @"\.\s*OpenConnection(?:Async)?\s*\("
        + @"|\.\s*CreateConnection\s*\("
        + @"|\bnew\s+NpgsqlConnection\s*\("
        + @"|\bNpgsqlDataSource\s*\.\s*Create\s*\("
        + @"|\bNpgsqlDataSourceBuilder\b"
        + @"|[dD]ataSource\s*\.\s*Create(?:Command|Batch)\s*\(")]
    private static partial Regex OpensAroundTheFactory();
}
