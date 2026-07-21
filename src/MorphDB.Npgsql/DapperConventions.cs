using System.Runtime.CompilerServices;
using Dapper;

namespace MorphDB.Npgsql;

/// <summary>
/// Maps snake_case columns to PascalCase properties for every Dapper read in this assembly.
/// <para>
/// This is a requirement of the assembly's SQL conventions, not of dependency injection — yet it
/// used to be set inside <c>AddMorphDbNpgsql</c>, which made correctness depend on who booted DI
/// first. Code that constructed a repository directly (a test fixture did) read <c>project_id</c>
/// into <c>ProjectId</c> as <c>Guid.Empty</c> with no error raised, provisioned schemas for the
/// empty id, and — because Dapper caches its deserializers per query — kept poisoning the same
/// reads after DI later set the flag. A module initializer runs before any type in this assembly
/// executes, so there is no window in which a repository can query under the wrong convention.
/// </para>
/// </summary>
internal static class DapperConventions
{
    // CA2255 warns against module initializers in libraries because callers cannot control when
    // they run. Here that is the point: the mapping must hold before the first Dapper query this
    // assembly issues, whoever issues it. The initializer is idempotent, dependency-free and sets
    // one process-wide flag this assembly's SQL is written against.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Apply() => DefaultTypeMap.MatchNamesWithUnderscores = true;
#pragma warning restore CA2255
}
