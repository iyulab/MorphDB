using System.Text.RegularExpressions;
using Dapper;
using MorphDB.Core.Security;
using Npgsql;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// PostgreSQL implementation of security policy service.
/// </summary>
public sealed partial class SecurityPolicyService : ISecurityPolicyService
{
    private readonly NpgsqlDataSource _dataSource;

    public SecurityPolicyService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<SecurityPolicy> CreatePolicyAsync(
        Guid tenantId,
        CreatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Get table ID
        var tableId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            """
            SELECT table_id FROM morphdb._morph_tables
            WHERE tenant_id = @TenantId AND name = @TableName
            """,
            new { TenantId = tenantId, request.TableName });

        if (!tableId.HasValue)
        {
            throw new InvalidOperationException($"Table '{request.TableName}' not found");
        }

        // Get next ordinal position
        var maxOrdinal = await connection.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT MAX(ordinal_position) FROM morphdb._morph_security_policies
            WHERE tenant_id = @TenantId AND table_id = @TableId
            """,
            new { TenantId = tenantId, TableId = tableId.Value });

        var now = DateTimeOffset.UtcNow;
        var policy = new SecurityPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TableId = tableId.Value,
            Name = request.Name,
            Description = request.Description,
            PolicyType = request.PolicyType,
            Expression = request.Expression,
            IsActive = true,
            OrdinalPosition = (maxOrdinal ?? 0) + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        await connection.ExecuteAsync(
            """
            INSERT INTO morphdb._morph_security_policies
            (id, tenant_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at)
            VALUES (@Id, @TenantId, @TableId, @Name, @Description, @PolicyType, @Expression, @IsActive, @OrdinalPosition, @CreatedAt, @UpdatedAt)
            """,
            new
            {
                policy.Id,
                policy.TenantId,
                policy.TableId,
                policy.Name,
                policy.Description,
                PolicyType = (int)policy.PolicyType,
                policy.Expression,
                policy.IsActive,
                policy.OrdinalPosition,
                policy.CreatedAt,
                policy.UpdatedAt
            });

        return policy;
    }

    public async Task<IReadOnlyList<SecurityPolicy>> GetPoliciesAsync(
        Guid tenantId,
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var records = await connection.QueryAsync<PolicyRecord>(
            """
            SELECT id, tenant_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at
            FROM morphdb._morph_security_policies
            WHERE tenant_id = @TenantId AND table_id = @TableId
            ORDER BY ordinal_position
            """,
            new { TenantId = tenantId, TableId = tableId });

        return records.Select(MapToPolicy).ToList();
    }

    public async Task<IReadOnlyList<SecurityPolicy>> GetPoliciesByTableNameAsync(
        Guid tenantId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var records = await connection.QueryAsync<PolicyRecord>(
            """
            SELECT p.id, p.tenant_id, p.table_id, p.name, p.description, p.policy_type, p.expression, p.is_active, p.ordinal_position, p.created_at, p.updated_at
            FROM morphdb._morph_security_policies p
            INNER JOIN morphdb._morph_tables t ON t.table_id = p.table_id AND t.tenant_id = p.tenant_id
            WHERE p.tenant_id = @TenantId AND t.name = @TableName
            ORDER BY p.ordinal_position
            """,
            new { TenantId = tenantId, TableName = tableName });

        return records.Select(MapToPolicy).ToList();
    }

    public async Task<SecurityPolicy?> GetPolicyAsync(
        Guid tenantId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var record = await connection.QueryFirstOrDefaultAsync<PolicyRecord>(
            """
            SELECT id, tenant_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at
            FROM morphdb._morph_security_policies
            WHERE tenant_id = @TenantId AND id = @PolicyId
            """,
            new { TenantId = tenantId, PolicyId = policyId });

        return record == null ? null : MapToPolicy(record);
    }

    public async Task<SecurityPolicy> UpdatePolicyAsync(
        Guid tenantId,
        Guid policyId,
        UpdatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetPolicyAsync(tenantId, policyId, cancellationToken);
        if (existing == null)
        {
            throw new InvalidOperationException($"Policy '{policyId}' not found");
        }

        var name = request.Name ?? existing.Name;
        var expression = request.Expression ?? existing.Expression;
        var isActive = request.IsActive ?? existing.IsActive;
        var description = request.Description ?? existing.Description;
        var updatedAt = DateTimeOffset.UtcNow;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE morphdb._morph_security_policies
            SET name = @Name, expression = @Expression, is_active = @IsActive, description = @Description, updated_at = @UpdatedAt
            WHERE tenant_id = @TenantId AND id = @PolicyId
            """,
            new
            {
                TenantId = tenantId,
                PolicyId = policyId,
                Name = name,
                Expression = expression,
                IsActive = isActive,
                Description = description,
                UpdatedAt = updatedAt
            });

        return new SecurityPolicy
        {
            Id = existing.Id,
            TenantId = existing.TenantId,
            TableId = existing.TableId,
            Name = name,
            Description = description,
            PolicyType = existing.PolicyType,
            Expression = expression,
            IsActive = isActive,
            OrdinalPosition = existing.OrdinalPosition,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = updatedAt
        };
    }

    public async Task DeletePolicyAsync(
        Guid tenantId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            DELETE FROM morphdb._morph_security_policies
            WHERE tenant_id = @TenantId AND id = @PolicyId
            """,
            new { TenantId = tenantId, PolicyId = policyId });
    }

    public async Task<string?> EvaluatePoliciesAsync(
        Guid tenantId,
        string tableName,
        PolicyType policyType,
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.BypassRls)
        {
            return null; // Service key bypasses RLS
        }

        var policies = await GetPoliciesByTableNameAsync(tenantId, tableName, cancellationToken);
        var applicablePolicies = policies
            .Where(p => p.IsActive && (p.PolicyType == policyType || p.PolicyType == PolicyType.All))
            .ToList();

        if (applicablePolicies.Count == 0)
        {
            return null;
        }

        // Combine policies with AND
        var expressions = applicablePolicies
            .Select(p => SubstituteVariables(p.Expression, context))
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        if (expressions.Count == 0)
        {
            return null;
        }

        return string.Join(" AND ", expressions.Select(e => $"({e})"));
    }

    private static string SubstituteVariables(string expression, SecurityContext context)
    {
        // Replace {{user_id}} with the actual user ID
        var result = PlaceholderRegex().Replace(expression, match =>
        {
            var placeholder = match.Groups[1].Value.ToLowerInvariant();
            return placeholder switch
            {
                "user_id" => context.UserId != null ? $"'{EscapeSqlString(context.UserId)}'" : "NULL",
                "email" => context.Email != null ? $"'{EscapeSqlString(context.Email)}'" : "NULL",
                "role" => context.Role != null ? $"'{EscapeSqlString(context.Role)}'" : "NULL",
                "tenant_id" => $"'{context.TenantId}'",
                "is_authenticated" => context.IsAuthenticated ? "true" : "false",
                _ when placeholder.StartsWith("claims.", StringComparison.Ordinal) => GetClaimValue(placeholder[7..], context),
                _ => match.Value // Keep original if not recognized
            };
        });

        return result;
    }

    private static string GetClaimValue(string claimName, SecurityContext context)
    {
        var value = context.GetClaim(claimName);
        return value != null ? $"'{EscapeSqlString(value)}'" : "NULL";
    }

    private static string EscapeSqlString(string value)
    {
        return value.Replace("'", "''");
    }

    [GeneratedRegex(@"\{\{(\w+(?:\.\w+)?)\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    private static SecurityPolicy MapToPolicy(PolicyRecord record)
    {
        return new SecurityPolicy
        {
            Id = record.Id,
            TenantId = record.TenantId,
            TableId = record.TableId,
            Name = record.Name,
            Description = record.Description,
            PolicyType = (PolicyType)record.PolicyType,
            Expression = record.Expression,
            IsActive = record.IsActive,
            OrdinalPosition = record.OrdinalPosition,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }

    private sealed record PolicyRecord(
        Guid Id,
        Guid TenantId,
        Guid TableId,
        string Name,
        string? Description,
        int PolicyType,
        string Expression,
        bool IsActive,
        int OrdinalPosition,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
