using MorphDB.Core.Models;
using MorphDB.Npgsql.Security;
using Npgsql;

namespace MorphDB.Npgsql.Pipeline;

/// <summary>
/// What a write PostgreSQL refused means to the caller, for every path that writes — the write
/// pipeline's executor and the bulk update and delete that run one statement over a filter. A path
/// that executes SQL without it answers a caller's mistake (a still-referenced row, a NULL into a
/// required column) as a 500.
/// </summary>
internal static class PostgresWriteRefusal
{
    /// <summary>
    /// The refusals PostgreSQL answers a write with that say something about the request rather
    /// than about this service: a physical constraint, or a row-level security policy.
    /// </summary>
    public static bool IsCallersMistake(PostgresException pg) =>
        pg.SqlState is PostgresErrorCodes.NotNullViolation
            or PostgresErrorCodes.UniqueViolation
            or PostgresErrorCodes.ForeignKeyViolation
            or PostgresErrorCodes.InsufficientPrivilege
            or PostgresErrorCodes.DatatypeMismatch
            or PostgresErrorCodes.InvalidTextRepresentation;

    /// <summary>
    /// Turns a physical NOT NULL / UNIQUE violation into the same kind of error the app-layer
    /// validators produce, naming the logical column — never the physical one — because internal
    /// names are not contract (hidden-layer principle).
    /// </summary>
    public static MorphDB.Core.Exceptions.MorphDbException Translate(
        PostgresException pg, TableMetadata table)
    {
        if (pg.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return RowLevelSecurityRefusal.ForWrite(table);
        }

        if (pg.SqlState == PostgresErrorCodes.NotNullViolation)
        {
            var logical = table.Columns.FirstOrDefault(c => c.PhysicalName == pg.ColumnName)?.LogicalName;
            return logical is null
                ? new MorphDB.Core.Exceptions.ValidationException("A required column does not allow null values.")
                : new MorphDB.Core.Exceptions.ValidationException(logical, "a value is required and cannot be null");
        }

        if (pg.SqlState is PostgresErrorCodes.DatatypeMismatch or PostgresErrorCodes.InvalidTextRepresentation)
        {
            // Reached when the type validator did not answer first. PostgreSQL's text names the physical
            // column, so it is not repeated.
            return new MorphDB.Core.Exceptions.ValidationException(
                "A value in this request cannot be read as its column's type.");
        }

        // The same code answers both directions of a foreign key. PostgreSQL names the referencing
        // table: this table when the row written points at a missing one, another table when the row
        // deleted (or whose key changed) is still pointed at.
        if (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation
            && pg.TableName is { } referencing
            && !string.Equals(referencing, table.PhysicalName, StringComparison.Ordinal))
        {
            return new MorphDB.Core.Exceptions.RowReferencedException(table.LogicalName);
        }

        if (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // Reached when the app-layer foreign-key validator did not answer first -- the physical
            // constraint is the backstop, and a backstop that surfaces as an opaque 500 tells the
            // caller their own mistake is our defect.
            return new MorphDB.Core.Exceptions.ValidationException(
                "A value in this request references a row that does not exist.");
        }

        return new MorphDB.Core.Exceptions.ValidationException(
            "A value in this request conflicts with an existing row: it violates a unique constraint.");
    }
}
