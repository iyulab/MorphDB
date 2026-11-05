using MorphDB.Core.Exceptions;
using Npgsql;

namespace MorphDB.Npgsql.Ddl;

/// <summary>
/// What a definition PostgreSQL refuses means to the caller, for every path that writes a caller's SQL
/// fragment into DDL -- a view's join condition and column expressions, an index predicate. A name that is
/// no column of the table, a quoted identifier the translator does not read, a syntax error, an operator
/// applied to types it does not take, a function an index predicate may not use: these are the request's
/// mistakes, so they answer <c>400 INVALID_EXPRESSION</c> like every other expression the request carries
/// instead of the opaque <c>500</c> the database error used to become. The answer names the kind of
/// refusal and never PostgreSQL's own text: that text can carry a physical name, and internal names are
/// not contract.
/// </summary>
internal static class DefinitionRefusal
{
    /// <summary>
    /// Runs <paramref name="run"/>; a definition mistake it raises becomes an <c>INVALID_EXPRESSION</c>
    /// <see cref="SchemaException"/> that opens with <paramref name="subject"/> and ends with
    /// <paramref name="hint"/>.
    /// </summary>
    public static async Task AsInvalidExpressionAsync(string subject, string hint, Func<Task> run)
    {
        try
        {
            await run().ConfigureAwait(false);
        }
        catch (PostgresException pg) when (Describe(pg) is not null)
        {
            throw new SchemaException("INVALID_EXPRESSION", $"{subject} is not valid ({Describe(pg)}): {hint}", pg);
        }
    }

    /// <summary>The kind of mistake a PostgreSQL error is, in words; <c>null</c> when it is not a definition mistake.</summary>
    public static string? Describe(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UndefinedTable => "an unknown table",
        PostgresErrorCodes.UndefinedColumn => "an unknown column",
        PostgresErrorCodes.AmbiguousColumn => "an ambiguous column",
        PostgresErrorCodes.SyntaxError => "a syntax error",
        PostgresErrorCodes.UndefinedFunction => "an operator or function that does not take these types",
        PostgresErrorCodes.DatatypeMismatch => "a type mismatch",
        PostgresErrorCodes.InvalidTextRepresentation or PostgresErrorCodes.InvalidDatetimeFormat => "a value that is not of its column's type",
        PostgresErrorCodes.GroupingError => "a column that is neither grouped nor aggregated",
        PostgresErrorCodes.InvalidObjectDefinition => "a definition PostgreSQL does not accept here",
        PostgresErrorCodes.FeatureNotSupported => "a construct that is not allowed here",
        _ => null,
    };
}
