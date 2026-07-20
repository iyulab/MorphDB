namespace MorphDB.Core.Security;

/// <summary>
/// Service for managing Row-Level Security policies.
/// </summary>
public interface ISecurityPolicyService
{
    /// <summary>
    /// Creates a new security policy.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="request">The create policy request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created security policy.</returns>
    Task<SecurityPolicy> CreatePolicyAsync(
        Guid projectId,
        CreatePolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all policies for a table.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableId">The table ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of security policies.</returns>
    Task<IReadOnlyList<SecurityPolicy>> GetPoliciesAsync(
        Guid projectId,
        Guid tableId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all policies for a table by name.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of security policies.</returns>
    Task<IReadOnlyList<SecurityPolicy>> GetPoliciesByTableNameAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific policy by ID.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="policyId">The policy ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The security policy or null if not found.</returns>
    Task<SecurityPolicy?> GetPolicyAsync(
        Guid projectId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a security policy.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="policyId">The policy ID.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated security policy.</returns>
    Task<SecurityPolicy> UpdatePolicyAsync(
        Guid projectId,
        Guid policyId,
        UpdatePolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a security policy.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="policyId">The policy ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeletePolicyAsync(
        Guid projectId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates policies for a table and returns the combined WHERE clause.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="policyType">The policy type to evaluate.</param>
    /// <param name="context">The security context for variable substitution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The combined WHERE clause or null if no policies apply.</returns>
    Task<string?> EvaluatePoliciesAsync(
        Guid projectId,
        string tableName,
        PolicyType policyType,
        SecurityContext context,
        CancellationToken cancellationToken = default);
}
