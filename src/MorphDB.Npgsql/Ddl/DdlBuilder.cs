using System.Globalization;
using System.Text;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Npgsql.Ddl;

/// <summary>
/// Builds DDL statements for PostgreSQL dynamic tables.
/// </summary>
public static class DdlBuilder
{
    /// <summary>
    /// Builds a CREATE TABLE statement.
    /// </summary>
    public static string BuildCreateTable(string physicalName, IReadOnlyList<ColumnDefinition> columns)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"CREATE TABLE {QuoteIdentifier(physicalName)} (");

        var columnDefs = new List<string>();
        var primaryKeyColumns = new List<string>();

        foreach (var col in columns)
        {
            var colDef = BuildColumnDefinition(col);
            columnDefs.Add($"    {colDef}");

            if (col.IsPrimaryKey)
            {
                primaryKeyColumns.Add(QuoteIdentifier(col.PhysicalName));
            }
        }

        sb.AppendLine(string.Join(",\n", columnDefs));

        if (primaryKeyColumns.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    ,PRIMARY KEY ({string.Join(", ", primaryKeyColumns)})");
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Builds an ALTER TABLE ADD COLUMN statement.
    /// </summary>
    public static string BuildAddColumn(string tablePhysicalName, ColumnDefinition column)
    {
        var colDef = BuildColumnDefinition(column);
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} ADD COLUMN {colDef}";
    }

    /// <summary>
    /// Builds an ALTER TABLE DROP COLUMN statement.
    /// </summary>
    public static string BuildDropColumn(string tablePhysicalName, string columnPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} DROP COLUMN IF EXISTS {QuoteIdentifier(columnPhysicalName)}";
    }

    /// <summary>
    /// Builds a DROP TABLE statement.
    /// </summary>
    public static string BuildDropTable(string physicalName)
    {
        return $"DROP TABLE IF EXISTS {QuoteIdentifier(physicalName)}";
    }

    /// <summary>
    /// Builds a CREATE INDEX statement.
    /// </summary>
    public static string BuildCreateIndex(IndexDefinition index)
    {
        var sb = new StringBuilder("CREATE ");

        if (index.IsUnique)
        {
            sb.Append("UNIQUE ");
        }

        sb.Append("INDEX ");
        sb.Append(QuoteIdentifier(index.PhysicalName));
        sb.Append(" ON ");
        sb.Append(QuoteIdentifier(index.TablePhysicalName));

        // Add USING clause for non-btree indexes
        if (index.IndexType != IndexType.BTree)
        {
            sb.Append(CultureInfo.InvariantCulture, $" USING {index.IndexType.ToString().ToLowerInvariant()}");
        }

        sb.Append(" (");

        var columnSpecs = index.Columns.Select(c =>
        {
            var spec = QuoteIdentifier(c.PhysicalName);
            if (c.Direction == SortDirection.Descending)
            {
                spec += " DESC";
            }
            if (c.NullsPosition == NullsPosition.First)
            {
                spec += " NULLS FIRST";
            }
            return spec;
        });

        sb.Append(string.Join(", ", columnSpecs));
        sb.Append(')');

        if (!string.IsNullOrWhiteSpace(index.WhereClause))
        {
            sb.Append(CultureInfo.InvariantCulture, $" WHERE {index.WhereClause}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a DROP INDEX statement.
    /// </summary>
    public static string BuildDropIndex(string indexPhysicalName)
    {
        return $"DROP INDEX IF EXISTS {QuoteIdentifier(indexPhysicalName)}";
    }

    /// <summary>
    /// Builds an ALTER TABLE ADD CONSTRAINT for foreign key.
    /// </summary>
    public static string BuildAddForeignKey(ForeignKeyDefinition fk)
    {
        var onDelete = MapReferentialAction(fk.OnDelete);
        var onUpdate = MapReferentialAction(fk.OnUpdate);

        return $"""
            ALTER TABLE {QuoteIdentifier(fk.SourceTablePhysicalName)}
            ADD CONSTRAINT {QuoteIdentifier(fk.ConstraintName)}
            FOREIGN KEY ({QuoteIdentifier(fk.SourceColumnPhysicalName)})
            REFERENCES {QuoteIdentifier(fk.TargetTablePhysicalName)} ({QuoteIdentifier(fk.TargetColumnPhysicalName)})
            ON DELETE {onDelete}
            ON UPDATE {onUpdate}
            """;
    }

    /// <summary>
    /// Builds an ALTER TABLE DROP CONSTRAINT statement.
    /// </summary>
    public static string BuildDropForeignKey(string tablePhysicalName, string constraintName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} DROP CONSTRAINT IF EXISTS {QuoteIdentifier(constraintName)}";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN SET NOT NULL statement.
    /// </summary>
    public static string BuildSetNotNull(string tablePhysicalName, string columnPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} SET NOT NULL";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN DROP NOT NULL statement.
    /// </summary>
    public static string BuildDropNotNull(string tablePhysicalName, string columnPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} DROP NOT NULL";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN SET DEFAULT statement.
    /// </summary>
    public static string BuildSetDefault(string tablePhysicalName, string columnPhysicalName, string defaultExpression)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} SET DEFAULT {defaultExpression}";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN DROP DEFAULT statement.
    /// </summary>
    public static string BuildDropDefault(string tablePhysicalName, string columnPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} DROP DEFAULT";
    }

    /// <summary>
    /// Builds an ALTER TABLE ADD CONSTRAINT UNIQUE statement.
    /// </summary>
    public static string BuildAddUniqueConstraint(string tablePhysicalName, string constraintName, string columnPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} ADD CONSTRAINT {QuoteIdentifier(constraintName)} UNIQUE ({QuoteIdentifier(columnPhysicalName)})";
    }

    /// <summary>
    /// Builds an ALTER TABLE RENAME COLUMN statement.
    /// </summary>
    public static string BuildRenameColumn(string tablePhysicalName, string oldPhysicalName, string newPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(tablePhysicalName)} RENAME COLUMN {QuoteIdentifier(oldPhysicalName)} TO {QuoteIdentifier(newPhysicalName)}";
    }

    /// <summary>
    /// Builds an ALTER TABLE RENAME TO statement.
    /// </summary>
    public static string BuildRenameTable(string oldPhysicalName, string newPhysicalName)
    {
        return $"ALTER TABLE {QuoteIdentifier(oldPhysicalName)} RENAME TO {QuoteIdentifier(newPhysicalName)}";
    }

    #region Private Helpers

    private static string BuildColumnDefinition(ColumnDefinition col)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{QuoteIdentifier(col.PhysicalName)} {col.NativeType}");

        if (!col.IsNullable && !col.IsPrimaryKey)
        {
            sb.Append(" NOT NULL");
        }

        if (col.DefaultExpression is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $" DEFAULT {col.DefaultExpression}");
        }

        if (col.CheckExpression is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $" CHECK ({col.CheckExpression})");
        }

        return sb.ToString();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static string MapReferentialAction(OnDeleteAction action) => action switch
    {
        OnDeleteAction.NoAction => "NO ACTION",
        OnDeleteAction.Cascade => "CASCADE",
        OnDeleteAction.SetNull => "SET NULL",
        OnDeleteAction.SetDefault => "SET DEFAULT",
        OnDeleteAction.Restrict => "RESTRICT",
        _ => "NO ACTION"
    };

    private static string MapReferentialAction(OnUpdateAction action) => action switch
    {
        OnUpdateAction.NoAction => "NO ACTION",
        OnUpdateAction.Cascade => "CASCADE",
        OnUpdateAction.SetNull => "SET NULL",
        OnUpdateAction.SetDefault => "SET DEFAULT",
        OnUpdateAction.Restrict => "RESTRICT",
        _ => "NO ACTION"
    };

    #endregion
}

/// <summary>
/// Column definition for DDL generation.
/// </summary>
public sealed record ColumnDefinition
{
    public required string PhysicalName { get; init; }
    public required string NativeType { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool IsPrimaryKey { get; init; }
    public bool IsUnique { get; init; }
    public string? DefaultExpression { get; init; }
    public string? CheckExpression { get; init; }

    /// <summary>
    /// Creates a ColumnDefinition from column metadata.
    /// </summary>
    public static ColumnDefinition FromMetadata(ColumnMetadata metadata)
    {
        var defaultExpr = metadata.DefaultValue;
        if (string.IsNullOrEmpty(defaultExpr))
        {
            defaultExpr = TypeMapper.GetDefaultExpression(metadata.DataType);
        }

        return new ColumnDefinition
        {
            PhysicalName = metadata.PhysicalName,
            NativeType = metadata.NativeType,
            IsNullable = metadata.IsNullable,
            IsPrimaryKey = metadata.IsPrimaryKey,
            IsUnique = metadata.IsUnique,
            DefaultExpression = defaultExpr,
            CheckExpression = metadata.CheckExpression
        };
    }
}

/// <summary>
/// Index definition for DDL generation.
/// </summary>
public sealed record IndexDefinition
{
    public required string PhysicalName { get; init; }
    public required string TablePhysicalName { get; init; }
    public required IReadOnlyList<IndexColumnInfo> Columns { get; init; }
    public IndexType IndexType { get; init; } = IndexType.BTree;
    public bool IsUnique { get; init; }
    public string? WhereClause { get; init; }
}

/// <summary>
/// Foreign key definition for DDL generation.
/// </summary>
public sealed record ForeignKeyDefinition
{
    public required string ConstraintName { get; init; }
    public required string SourceTablePhysicalName { get; init; }
    public required string SourceColumnPhysicalName { get; init; }
    public required string TargetTablePhysicalName { get; init; }
    public required string TargetColumnPhysicalName { get; init; }
    public OnDeleteAction OnDelete { get; init; } = OnDeleteAction.NoAction;
    public OnUpdateAction OnUpdate { get; init; } = OnUpdateAction.NoAction;
}
