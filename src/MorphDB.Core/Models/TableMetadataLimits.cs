using MorphDB.Core.Exceptions;

namespace MorphDB.Core.Models;

/// <summary>
/// The bounds on a table's creator notes (<see cref="TableMetadata.Metadata"/>): enough to name an
/// owner and an origin, not a document store. A request outside them is refused rather than trimmed.
/// </summary>
public static class TableMetadataLimits
{
    /// <summary>Entries a table may carry.</summary>
    public const int MaxEntries = 32;

    /// <summary>Characters in a key.</summary>
    public const int MaxKeyLength = 128;

    /// <summary>Characters in a value.</summary>
    public const int MaxValueLength = 1024;

    /// <summary>Refuses notes outside the bounds, naming the first problem.</summary>
    /// <exception cref="ValidationException">The notes are outside the bounds.</exception>
    public static void EnsureValid(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (metadata.Count > MaxEntries)
        {
            throw new ValidationException("metadata", $"a table carries at most {MaxEntries} metadata entries; {metadata.Count} were given.");
        }

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
            {
                throw new ValidationException("metadata", $"a metadata key is 1 to {MaxKeyLength} characters and not blank.");
            }

            if (value is null || value.Length > MaxValueLength)
            {
                throw new ValidationException("metadata", $"the value of metadata key '{key}' is a string of at most {MaxValueLength} characters.");
            }
        }
    }
}
