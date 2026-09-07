namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Locates a file under the repository's <c>docs/</c> from wherever the test binary runs. Every
/// docs-parity gate reads the same reference the consumer reads, so they share one way of finding
/// it rather than each keeping a copy of the directory walk.
/// </summary>
public static class DocsFiles
{
    public static string Find(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"docs/{name} not found above {AppContext.BaseDirectory}");
    }

    public static string ReadApiReference() => File.ReadAllText(Find("API.md"));
}
