using Microsoft.OData.Edm;
using MorphDB.Core.Models;

namespace MorphDB.Service.OData;

/// <summary>
/// Builds OData EDM models dynamically from MorphDB table metadata.
/// </summary>
public static class DynamicEdmModelBuilder
{
    /// <summary>
    /// Builds an EDM model from the provided table metadata.
    /// </summary>
    public static IEdmModel BuildModel(IReadOnlyList<TableMetadata> tables)
    {
        var model = new EdmModel();

        var entityContainer = new EdmEntityContainer("MorphDB", "MorphDBContainer");
        model.AddElement(entityContainer);

        var entityTypes = new Dictionary<string, EdmEntityType>(StringComparer.OrdinalIgnoreCase);

        // First pass: Create entity types with properties
        foreach (var table in tables.Where(t => t.IsActive))
        {
            var entityTypeName = ToPascalCase(table.LogicalName);
            var entityType = new EdmEntityType("MorphDB", entityTypeName);

            // Add system properties
            var idProperty = entityType.AddStructuralProperty("id", EdmPrimitiveTypeKind.Guid, false);
            entityType.AddKeys(idProperty);
            entityType.AddStructuralProperty("tenant_id", EdmPrimitiveTypeKind.Guid, false);
            entityType.AddStructuralProperty("created_at", EdmPrimitiveTypeKind.DateTimeOffset, true);
            entityType.AddStructuralProperty("updated_at", EdmPrimitiveTypeKind.DateTimeOffset, true);

            // Add user-defined columns
            foreach (var column in table.Columns.Where(c => c.IsActive && !IsSystemColumn(c.LogicalName)))
            {
                var edmType = MapToEdmType(column.DataType);
                entityType.AddStructuralProperty(
                    column.LogicalName,
                    edmType,
                    column.IsNullable);
            }

            model.AddElement(entityType);
            entityTypes[table.LogicalName] = entityType;

            // Add entity set
            entityContainer.AddEntitySet(entityTypeName, entityType);
        }

        // Second pass: Add navigation properties for relations
        foreach (var table in tables.Where(t => t.IsActive))
        {
            if (!entityTypes.TryGetValue(table.LogicalName, out var sourceEntityType))
                continue;

            foreach (var relation in table.Relations.Where(r => r.IsActive))
            {
                var targetTable = tables.FirstOrDefault(t => t.TableId == relation.TargetTableId);
                if (targetTable == null || !entityTypes.TryGetValue(targetTable.LogicalName, out var targetEntityType))
                    continue;

                var navPropertyName = ToPascalCase(relation.LogicalName);

                // Create navigation property based on relation type
                switch (relation.RelationType)
                {
                    case RelationType.OneToOne:
                        sourceEntityType.AddUnidirectionalNavigation(
                            new EdmNavigationPropertyInfo
                            {
                                Name = navPropertyName,
                                Target = targetEntityType,
                                TargetMultiplicity = EdmMultiplicity.ZeroOrOne
                            });
                        break;

                    case RelationType.OneToMany:
                        sourceEntityType.AddUnidirectionalNavigation(
                            new EdmNavigationPropertyInfo
                            {
                                Name = navPropertyName,
                                Target = targetEntityType,
                                TargetMultiplicity = EdmMultiplicity.Many
                            });
                        break;

                    case RelationType.ManyToMany:
                        // Many-to-many requires a join table, handle as collection
                        sourceEntityType.AddUnidirectionalNavigation(
                            new EdmNavigationPropertyInfo
                            {
                                Name = navPropertyName,
                                Target = targetEntityType,
                                TargetMultiplicity = EdmMultiplicity.Many
                            });
                        break;
                }
            }
        }

        return model;
    }

    /// <summary>
    /// Maps MorphDB data type to EDM primitive type kind.
    /// </summary>
    private static EdmPrimitiveTypeKind MapToEdmType(MorphDataType dataType)
    {
        return dataType switch
        {
            MorphDataType.Text => EdmPrimitiveTypeKind.String,
            MorphDataType.LongText => EdmPrimitiveTypeKind.String,
            MorphDataType.Integer => EdmPrimitiveTypeKind.Int32,
            MorphDataType.BigInteger => EdmPrimitiveTypeKind.Int64,
            MorphDataType.Decimal => EdmPrimitiveTypeKind.Decimal,
            MorphDataType.Boolean => EdmPrimitiveTypeKind.Boolean,
            MorphDataType.Date => EdmPrimitiveTypeKind.Date,
            MorphDataType.DateTime => EdmPrimitiveTypeKind.DateTimeOffset,
            MorphDataType.Time => EdmPrimitiveTypeKind.TimeOfDay,
            MorphDataType.Uuid => EdmPrimitiveTypeKind.Guid,
            MorphDataType.Json => EdmPrimitiveTypeKind.String,
            MorphDataType.Email => EdmPrimitiveTypeKind.String,
            MorphDataType.Url => EdmPrimitiveTypeKind.String,
            MorphDataType.Phone => EdmPrimitiveTypeKind.String,
            MorphDataType.SingleSelect => EdmPrimitiveTypeKind.String,
            MorphDataType.MultiSelect => EdmPrimitiveTypeKind.String,
            MorphDataType.CreatedTime => EdmPrimitiveTypeKind.DateTimeOffset,
            MorphDataType.ModifiedTime => EdmPrimitiveTypeKind.DateTimeOffset,
            MorphDataType.CreatedBy => EdmPrimitiveTypeKind.Guid,
            MorphDataType.ModifiedBy => EdmPrimitiveTypeKind.Guid,
            MorphDataType.Array => EdmPrimitiveTypeKind.String, // JSON serialized
            MorphDataType.Relation => EdmPrimitiveTypeKind.Guid,
            MorphDataType.Rollup => EdmPrimitiveTypeKind.String,
            MorphDataType.Formula => EdmPrimitiveTypeKind.String,
            MorphDataType.Attachment => EdmPrimitiveTypeKind.String,
            _ => EdmPrimitiveTypeKind.String
        };
    }

    private static bool IsSystemColumn(string columnName)
    {
        return columnName is "id" or "tenant_id" or "created_at" or "updated_at";
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = name.Split('_', '-', ' ');
        return string.Concat(parts.Select(p =>
            p.Length > 0
                ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()
                : p));
    }
}
