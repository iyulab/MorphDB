using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// The naming rule for a project's PostgreSQL schemas, and the parsing of names it produced.
/// <para>
/// A new project's schemas are named from its whole id — <c>p_{32 hex digits}_sys</c> for the system
/// schema and <c>p_{32 hex digits}_dat</c> for the data schema — so two projects never ask for the same
/// schemas. The rule applies when a project is created; the names are then recorded with the project
/// and every later operation reads the recorded ones through
/// <see cref="IProjectRepository.GetSchemaNamesAsync"/>. A project created before the rule changed keeps
/// the schemas it was given (named from the first eight hex digits of its id), which is why nothing
/// here computes a name for an existing project.
/// </para>
/// </summary>
public interface ISchemaNameResolver
{
    /// <summary>
    /// The schema names a project created under <paramref name="projectId"/> is given. Only the code
    /// creating the project calls this; everything else reads the recorded names.
    /// </summary>
    SchemaNames NameSchemasForNewProject(Guid projectId);

    /// <summary>
    /// Recovers the project id from a schema name this rule produced for a new project.
    /// </summary>
    /// <param name="schemaName">The schema name to parse.</param>
    /// <param name="projectId">The project id the name was made from, when parsing succeeds.</param>
    /// <returns>
    /// True for a name carrying a whole id. A name from the earlier eight-digit rule carries only part
    /// of the id and returns false — the project it belongs to is found through its recorded names.
    /// </returns>
    bool TryParseSchemaName(string schemaName, out Guid projectId);

    /// <summary>
    /// Determines the schema type from a schema name — either naming rule.
    /// </summary>
    SchemaType GetSchemaType(string schemaName);

    /// <summary>
    /// Generates a fully qualified object name (schema.object).
    /// </summary>
    string QualifyName(string schemaName, string objectName);

    /// <summary>
    /// Validates that a schema name follows MorphDB naming conventions.
    /// </summary>
    bool IsValidSchemaName(string schemaName);

    /// <summary>
    /// Gets the global control plane schema name (morphdb).
    /// </summary>
    string GlobalSchema { get; }
}

/// <summary>
/// Contains both schema names for a project.
/// </summary>
public readonly record struct SchemaNames(string SystemSchema, string DataSchema);

/// <summary>
/// Types of PostgreSQL schemas managed by MorphDB.
/// </summary>
public enum SchemaType
{
    /// <summary>
    /// Unknown or unrecognized schema.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Global control plane schema (morphdb).
    /// </summary>
    Global = 1,

    /// <summary>
    /// Project system schema (p_{id}_sys).
    /// </summary>
    ProjectSystem = 2,

    /// <summary>
    /// Project data schema (p_{id}_dat).
    /// </summary>
    ProjectData = 3,

    /// <summary>
    /// PostgreSQL public schema.
    /// </summary>
    Public = 4
}
