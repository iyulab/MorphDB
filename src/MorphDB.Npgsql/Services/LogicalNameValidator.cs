using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// Validates logical names for MorphDB entities and columns.
/// Entity names (tables, views, indexes, relations) allow underscore prefix
/// but block the reserved "_morph_" prefix.
/// Column names block all underscore prefixes and the project column (reserved for system columns).
/// </summary>
public static class LogicalNameValidator
{
    private const string ReservedPrefix = "_morph_";
    private const int MaxNameLength = 255;

    /// <summary>
    /// Validates a logical name for entities (tables, views, indexes, relations).
    /// Allows underscore prefix but blocks the "_morph_" reserved prefix.
    /// </summary>
    public static void ValidateEntityName(string name, string entityType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SchemaException("INVALID_NAME", $"{entityType} name cannot be empty.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new SchemaException("INVALID_NAME", $"{entityType} name cannot exceed {MaxNameLength} characters.");
        }

        if (name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new SchemaException("RESERVED_NAME", $"{entityType} name cannot start with '{ReservedPrefix}' (reserved for system).");
        }
    }

    /// <summary>
    /// Validates a logical name for columns.
    /// Blocks all underscore prefixes (reserved for system columns like _id, _created_at) and
    /// <see cref="SystemColumns.ProjectId"/>, in any case: every table already has that column, and column
    /// names are looked up ignoring case.
    /// </summary>
    public static void ValidateColumnName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SchemaException("INVALID_NAME", "Column name cannot be empty.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new SchemaException("INVALID_NAME", $"Column name cannot exceed {MaxNameLength} characters.");
        }

        if (name.StartsWith('_'))
        {
            throw new SchemaException("INVALID_NAME", "Column name cannot start with underscore (reserved for system columns).");
        }

        if (string.Equals(name, SystemColumns.ProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SchemaException("RESERVED_NAME", $"Column name '{name}' is reserved for system columns.");
        }
    }
}
