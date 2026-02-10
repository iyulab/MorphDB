using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies default values to fields that are missing or null.
/// Handles static, computed, and context-based defaults.
/// </summary>
public sealed class DefaultValueApplier : ITransformer
{
    public int Order => PipelineOrder.DefaultValueApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplyDefaults
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        foreach (var column in context.Table.Columns.Where(c => c.IsActive))
        {
            // Skip if value already provided
            if (context.Data.TryGetValue(column.LogicalName, out var existingValue) && existingValue is not null)
            {
                continue;
            }

            // Apply default based on type
            var defaultValue = GetDefaultValue(column, context);
            if (defaultValue is not null)
            {
                context.Data[column.LogicalName] = defaultValue;
            }
        }

        return Task.CompletedTask;
    }

    private static object? GetDefaultValue(ColumnMetadata column, IWriteContext context)
    {
        return column.DefaultType switch
        {
            DefaultValueType.Static => ParseStaticDefault(column.DefaultValue, column.DataType),
            DefaultValueType.ContextBased => GetContextBasedDefault(column, context),
            DefaultValueType.Computed => GetComputedDefault(column, context),
            // DbFunction and AutoIncrement are handled by PostgreSQL
            _ => null
        };
    }

    private static object? ParseStaticDefault(string? defaultValue, MorphDataType dataType)
    {
        if (string.IsNullOrEmpty(defaultValue))
            return null;

        return dataType switch
        {
            MorphDataType.Text or MorphDataType.LongText or MorphDataType.Email or MorphDataType.Url or MorphDataType.Phone
                => defaultValue,
            MorphDataType.Integer => int.TryParse(defaultValue, out var i) ? i : null,
            MorphDataType.BigInteger => long.TryParse(defaultValue, out var l) ? l : null,
            MorphDataType.Decimal => decimal.TryParse(defaultValue, out var d) ? d : null,
            MorphDataType.Boolean => bool.TryParse(defaultValue, out var b) ? b : null,
            MorphDataType.Uuid => Guid.TryParse(defaultValue, out var g) ? g : null,
            MorphDataType.Date or MorphDataType.DateTime =>
                DateTimeOffset.TryParse(defaultValue, out var dt) ? dt : null,
            _ => defaultValue
        };
    }

    private static object? GetContextBasedDefault(ColumnMetadata column, IWriteContext context)
    {
        // Parse the default value as a context key
        var contextKey = column.DefaultValue?.Trim().ToLowerInvariant();

        return contextKey switch
        {
            "{{user_id}}" or "{{userid}}" => context.SecurityContext?.UserId,
            "{{tenant_id}}" or "{{tenantid}}" => context.TenantId,
            "{{user_email}}" or "{{email}}" => context.SecurityContext?.Email,
            "{{now}}" or "{{current_timestamp}}" => DateTimeOffset.UtcNow,
            "{{today}}" or "{{current_date}}" => DateTimeOffset.UtcNow.Date,
            "{{uuid}}" or "{{new_uuid}}" => Guid.NewGuid(),
            _ => null
        };
    }

    private static object? GetComputedDefault(ColumnMetadata column, IWriteContext context)
    {
        // Simple computed expressions: "field1 + field2", "field1 * 0.1", etc.
        var expression = column.DefaultValue;
        if (string.IsNullOrEmpty(expression))
            return null;

        // For now, just handle simple field references
        // More complex expressions would need a proper expression parser
        if (context.Data.TryGetValue(expression, out var value))
        {
            return value;
        }

        return null;
    }
}
