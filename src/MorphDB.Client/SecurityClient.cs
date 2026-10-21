using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for row-level security policies and issued secrets.
/// <para>
/// Policies belong to the client's project. Secrets are not scoped to a project and every secret call
/// needs the master secret (<see cref="MorphDBClientOptions.Secret"/>): an issued secret, whatever its
/// role, is refused with <see cref="MorphDBAuthorizationException"/>, and a service started without a
/// master secret answers <c>503</c> to all of them.
/// </para>
/// </summary>
public sealed class SecurityClient
{
    private readonly HttpClient _httpClient;

    internal SecurityClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// The security policies on a table.
    /// </summary>
    public async Task<IReadOnlyList<SecurityPolicyInfo>> GetPoliciesAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/security/policies/{Uri.EscapeDataString(tableName)}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListSecurityPolicyInfo, cancellationToken) ?? [];
    }

    /// <summary>
    /// Creates a security policy on a table.
    /// </summary>
    public async Task<SecurityPolicyInfo> CreatePolicyAsync(CreateSecurityPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/security/policies", request, MorphDBJsonContext.Default.CreateSecurityPolicyRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.SecurityPolicyInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize policy response");
    }

    /// <summary>
    /// Changes a security policy. Members of <paramref name="request"/> left null are left as they are.
    /// </summary>
    public async Task<SecurityPolicyInfo> UpdatePolicyAsync(Guid policyId, UpdateSecurityPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"/api/security/policies/{policyId}", request, MorphDBJsonContext.Default.UpdateSecurityPolicyRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.SecurityPolicyInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize policy response");
    }

    /// <summary>
    /// Deletes a security policy.
    /// </summary>
    public async Task DeletePolicyAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/security/policies/{policyId}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Issues a secret. Its plaintext is in the result and cannot be read again.
    /// </summary>
    public async Task<IssuedSecret> IssueSecretAsync(IssueSecretRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/security/secrets", request, MorphDBJsonContext.Default.IssueSecretRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.IssuedSecret, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize secret response");
    }

    /// <summary>
    /// The issued secrets, revoked ones included. Never returns a plaintext or a hash.
    /// </summary>
    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/security/secrets", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListSecretInfo, cancellationToken) ?? [];
    }

    /// <summary>
    /// Revokes a secret; requests presenting it are refused from now on. The service keeps the row so
    /// audit records still name it. Returns false when there is no such secret.
    /// </summary>
    public async Task<bool> RevokeSecretAsync(Guid secretId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/security/secrets/{secretId}", cancellationToken);
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return false;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return true;
    }
}
