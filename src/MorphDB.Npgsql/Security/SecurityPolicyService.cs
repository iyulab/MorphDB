using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// PostgreSQL implementation of security policy service.
/// </summary>
/// <remarks>
/// A policy is a row in <c>morphdb._morph_security_policies</c>, and PostgreSQL enforces it: every
/// change to a stored policy is projected onto the table's native row-level security in the same
/// transaction (<see cref="NativePolicyProjector"/>), so there is no moment at which the two disagree.
/// </remarks>
public sealed class SecurityPolicyService : ISecurityPolicyService
{
    private readonly MorphConnectionFactory _connections;
    private readonly IMetadataRepository _metadataRepository;
    private readonly NativePolicyProjector _projector;

    public SecurityPolicyService(
        MorphConnectionFactory connections,
        IMetadataRepository metadataRepository,
        ILogger<SecurityPolicyService>? logger = null)
    {
        _connections = connections;
        _metadataRepository = metadataRepository;
        _projector = new NativePolicyProjector(
            connections, metadataRepository, logger ?? NullLogger<SecurityPolicyService>.Instance);
    }

    public async Task<SecurityPolicy> CreatePolicyAsync(
        Guid projectId,
        CreatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        // A policy expression becomes part of a native policy, so it is a caller-authored string
        // that reaches SQL verbatim — the same category as a CHECK predicate, and subject to the
        // same gate. Rejecting it here quotes the caller their own text.
        InlineExpressionValidator.Validate(request.Expression, "Policy");

        // The type arrives as a name or as a number, and a number need not be one of the types. A
        // policy of a type nothing knows how to enforce is not stored.
        if (!Enum.IsDefined(request.PolicyType))
        {
            throw new ValidationException(
                "policyType",
                $"'{request.PolicyType}' is not a policy type; use one of {string.Join(", ", Enum.GetNames<PolicyType>())}.");
        }

        var table = await _metadataRepository.GetTableByNameAsync(projectId, request.TableName, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.TableName);

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await using var tableLock = await NativePolicyProjector.LockAsync(connection, table, cancellationToken);
        table = await ReadUnderLockAsync(table, cancellationToken)
            ?? throw new TableNotFoundException(request.TableName);

        // Get next ordinal position
        var maxOrdinal = await connection.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT MAX(ordinal_position) FROM morphdb._morph_security_policies
            WHERE project_id = @ProjectId AND table_id = @TableId
            """,
            new { ProjectId = projectId, TableId = table.TableId });

        var now = DateTimeOffset.UtcNow;
        var policy = new SecurityPolicy
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            TableId = table.TableId,
            Name = request.Name,
            Description = request.Description,
            PolicyType = request.PolicyType,
            Expression = request.Expression,
            IsActive = true,
            OrdinalPosition = (maxOrdinal ?? 0) + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Translating the expression is also what proves it is a predicate over this table, so a
        // policy that cannot be enforced is refused before anything is written.
        var stored = await NativePolicyProjector.LoadStoredAsync(connection, table.TableId, cancellationToken);
        var plan = await _projector.PlanAsync(
            connection, table, [.. stored, Stored(policy)], strictPolicyId: policy.Id, cancellationToken);

        // The row and the native policy it stands for, or neither.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await MetadataConstraintTranslation.AsDuplicateNameAsync(
            "SecurityPolicy",
            policy.Name,
            () => connection.ExecuteAsync(
                """
                INSERT INTO morphdb._morph_security_policies
                (id, project_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at)
                VALUES (@Id, @ProjectId, @TableId, @Name, @Description, @PolicyType, @Expression, @IsActive, @OrdinalPosition, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    policy.Id,
                    policy.ProjectId,
                    policy.TableId,
                    policy.Name,
                    policy.Description,
                    PolicyType = (int)policy.PolicyType,
                    policy.Expression,
                    policy.IsActive,
                    policy.OrdinalPosition,
                    policy.CreatedAt,
                    policy.UpdatedAt
                },
                transaction));

        await _projector.ApplyAsync(connection, transaction, table, plan, cancellationToken);

        // Asked last, once the policy's own DDL holds the table: a materialized view being created
        // over the table at this moment either committed before that lock was granted, and is found
        // here, or waits for this transaction and then finds the policy.
        await MaterializedViewPolicyGuard.EnsureNoMaterializedViewReadsAsync(
            connection, transaction, table, "created on", cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return policy;
    }

    public async Task<IReadOnlyList<SecurityPolicy>> GetPoliciesAsync(
        Guid projectId,
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var records = await connection.QueryAsync<PolicyRecord>(
            """
            SELECT id, project_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at
            FROM morphdb._morph_security_policies
            WHERE project_id = @ProjectId AND table_id = @TableId
            ORDER BY ordinal_position
            """,
            new { ProjectId = projectId, TableId = tableId });

        return records.Select(MapToPolicy).ToList();
    }

    public async Task<IReadOnlyList<SecurityPolicy>> GetPoliciesByTableNameAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var records = await connection.QueryAsync<PolicyRecord>(
            """
            SELECT p.id, p.project_id, p.table_id, p.name, p.description, p.policy_type, p.expression, p.is_active, p.ordinal_position, p.created_at, p.updated_at
            FROM morphdb._morph_security_policies p
            INNER JOIN morphdb._morph_tables t ON t.table_id = p.table_id AND t.project_id = p.project_id
            WHERE p.project_id = @ProjectId AND t.logical_name = @TableName AND t.is_active = true
            ORDER BY p.ordinal_position
            """,
            new { ProjectId = projectId, TableName = tableName });

        return records.Select(MapToPolicy).ToList();
    }

    public async Task<SecurityPolicy?> GetPolicyAsync(
        Guid projectId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var record = await connection.QueryFirstOrDefaultAsync<PolicyRecord>(
            """
            SELECT id, project_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at
            FROM morphdb._morph_security_policies
            WHERE project_id = @ProjectId AND id = @PolicyId
            """,
            new { ProjectId = projectId, PolicyId = policyId });

        return record == null ? null : MapToPolicy(record);
    }

    public async Task<SecurityPolicy> UpdatePolicyAsync(
        Guid projectId,
        Guid policyId,
        UpdatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetPolicyAsync(projectId, policyId, cancellationToken);
        if (existing == null)
        {
            throw new NotFoundException("Policy", policyId.ToString());
        }

        var name = request.Name ?? existing.Name;
        var expression = request.Expression ?? existing.Expression;
        if (request.Expression is not null)
        {
            InlineExpressionValidator.Validate(request.Expression, "Policy");
        }

        // A deleted table keeps its policy rows but has nothing left to enforce them on. Its
        // policies can still be renamed or described; a new expression has no table to be checked
        // against.
        var table = await _metadataRepository.GetTableByIdAsync(existing.TableId, includeColumns: true, cancellationToken);
        if (table is null && request.Expression is not null)
        {
            throw new NotFoundException("Table", existing.TableId.ToString());
        }

        var isActive = request.IsActive ?? existing.IsActive;
        var description = request.Description ?? existing.Description;
        var updatedAt = DateTimeOffset.UtcNow;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await using var tableLock = table is null
            ? null
            : await NativePolicyProjector.LockAsync(connection, table, cancellationToken);
        if (table is not null)
        {
            table = await ReadUnderLockAsync(table, cancellationToken);
            if (table is null && request.Expression is not null)
            {
                throw new NotFoundException("Table", existing.TableId.ToString());
            }
        }

        // An expression the caller is writing now is theirs to get right, so it is verified even
        // when the policy is inactive — and so is one the caller is switching on. Otherwise a
        // policy stored earlier is projected as it stands.
        IReadOnlyList<NativePolicyProjector.NativePolicy>? plan = null;
        if (table is not null)
        {
            var stored = await NativePolicyProjector.LoadStoredAsync(connection, table.TableId, cancellationToken);
            var updated = stored
                .Select(p => p.Id == policyId ? p with { Name = name, Expression = expression, IsActive = isActive } : p)
                .ToList();
            var callersToGetRight = request.Expression is not null || (isActive && !existing.IsActive);
            plan = await _projector.PlanAsync(
                connection, table, updated, callersToGetRight ? policyId : null, cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE morphdb._morph_security_policies
            SET name = @Name, expression = @Expression, is_active = @IsActive, description = @Description, updated_at = @UpdatedAt
            WHERE project_id = @ProjectId AND id = @PolicyId
            """,
            new
            {
                ProjectId = projectId,
                PolicyId = policyId,
                Name = name,
                Expression = expression,
                IsActive = isActive,
                Description = description,
                UpdatedAt = updatedAt
            },
            transaction);

        if (table is not null && plan is not null)
        {
            await _projector.ApplyAsync(connection, transaction, table, plan, cancellationToken);

            // Switching a policy back on gives the table a policy, as creating one does.
            if (isActive && !existing.IsActive)
            {
                await MaterializedViewPolicyGuard.EnsureNoMaterializedViewReadsAsync(
                    connection, transaction, table, "switched on for", cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return new SecurityPolicy
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
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
        Guid projectId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetPolicyAsync(projectId, policyId, cancellationToken);
        if (existing is null)
        {
            return;
        }

        var table = await _metadataRepository.GetTableByIdAsync(existing.TableId, includeColumns: true, cancellationToken);

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await using var tableLock = table is null
            ? null
            : await NativePolicyProjector.LockAsync(connection, table, cancellationToken);
        if (table is not null)
        {
            table = await ReadUnderLockAsync(table, cancellationToken);
        }

        IReadOnlyList<NativePolicyProjector.NativePolicy>? plan = null;
        if (table is not null)
        {
            var stored = await NativePolicyProjector.LoadStoredAsync(connection, table.TableId, cancellationToken);
            plan = await _projector.PlanAsync(
                connection, table, stored.Where(p => p.Id != policyId).ToList(), strictPolicyId: null, cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            DELETE FROM morphdb._morph_security_policies
            WHERE project_id = @ProjectId AND id = @PolicyId
            """,
            new { ProjectId = projectId, PolicyId = policyId },
            transaction);

        if (table is not null && plan is not null)
        {
            await _projector.ApplyAsync(connection, transaction, table, plan, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The table as it stands once the caller holds its policy lock. A column renamed or added while
    /// the caller waited for the lock is then the one a policy is checked against, not the column
    /// as it was before. <c>null</c> when the table was deleted meanwhile.
    /// </summary>
    private Task<TableMetadata?> ReadUnderLockAsync(TableMetadata table, CancellationToken cancellationToken) =>
        _metadataRepository.GetTableByIdAsync(table.TableId, includeColumns: true, cancellationToken);

    private static NativePolicyProjector.StoredPolicy Stored(SecurityPolicy policy) =>
        new(policy.Id, policy.Name, policy.PolicyType, policy.Expression, policy.IsActive);

    private static SecurityPolicy MapToPolicy(PolicyRecord record)
    {
        return new SecurityPolicy
        {
            Id = record.Id,
            ProjectId = record.ProjectId,
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

    /// <summary>
    /// A row of _morph_security_policies. Settable properties, not a positional record: Dapper
    /// applies MatchNamesWithUnderscores when it maps columns onto properties, but a positional
    /// record forces it to look for a constructor whose parameters are named exactly like the
    /// columns — which no C# record can be — so every read threw on materialization instead.
    /// </summary>
    private sealed class PolicyRecord
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid TableId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int PolicyType { get; set; }
        public string Expression { get; set; } = "";
        public bool IsActive { get; set; }
        public int OrdinalPosition { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
