using System.Security.Cryptography;
using Dapper;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Security;
using Npgsql;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// PostgreSQL implementation of <see cref="ISecretService"/>.
/// </summary>
public sealed class SecretService : ISecretService
{
    /// <summary>
    /// The plaintext prefix. It carries no privilege — it exists so a leaked string is recognizable
    /// as a MorphDB credential by secret scanners and by the person who finds it.
    /// </summary>
    public const string Prefix = "mdb_";

    private const int EntropyBytes = 32;

    private readonly NpgsqlDataSource _dataSource;
    private readonly SecretOptions _options;

    public SecretService(NpgsqlDataSource dataSource, SecretOptions options)
    {
        _dataSource = dataSource;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<IssuedSecret> IssueAsync(
        IssueSecretRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "A secret must be named.");
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            throw new ValidationException("role", "A secret must carry a role.");
        }

        // The bootstrap stays acyclic only if privilege cannot originate in-band. See
        // SecretRoles.Reserved for why this is an invariant rather than a policy.
        if (!SecretRoles.IsIssuable(request.Role))
        {
            throw new ValidationException(
                "role",
                $"The role '{request.Role}' is reserved and cannot be issued. The master secret is " +
                "injected at start-up and is never minted through the API.");
        }

        var plaintext = GeneratePlaintext();
        var secret = new Secret
        {
            SecretId = Guid.NewGuid(),
            Name = request.Name,
            SecretHash = Hash(plaintext),
            Role = request.Role,
            ProjectId = request.ProjectId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            INSERT INTO morphdb._morph_secrets
                (secret_id, name, secret_hash, role, project_id, is_active, created_at)
            VALUES
                (@SecretId, @Name, @SecretHash, @Role, @ProjectId, true, @CreatedAt)
            """,
            secret);

        return new IssuedSecret { Secret = secret, Plaintext = plaintext };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Secret>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // secret_hash is not selected: listing is a management operation, and a hash is the only
        // thing here worth stealing.
        var rows = await connection.QueryAsync<Secret>(
            """
            SELECT secret_id AS SecretId, name AS Name, role AS Role, project_id AS ProjectId,
                   is_active AS IsActive, created_at AS CreatedAt, revoked_at AS RevokedAt
            FROM morphdb._morph_secrets
            ORDER BY created_at DESC
            """);

        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeAsync(Guid secretId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            """
            UPDATE morphdb._morph_secrets
            SET is_active = false, revoked_at = @RevokedAt
            WHERE secret_id = @SecretId AND is_active = true
            """,
            new { SecretId = secretId, RevokedAt = DateTimeOffset.UtcNow });

        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<Secret?> AuthenticateAsync(
        string presented,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return null;
        }

        // The master secret is configuration, not data. Answering it before touching the database
        // is what lets an operator reach a database whose control plane is unreachable or empty --
        // which is the state every fresh installation starts in.
        if (_options.MasterSecret is { } master && FixedTimeEquals(presented, master))
        {
            return new Secret
            {
                SecretId = Guid.Empty,
                Name = "master",
                Role = SecretRoles.Master,
                ProjectId = null,
                IsActive = true
            };
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<Secret>(
            """
            SELECT secret_id AS SecretId, name AS Name, role AS Role, project_id AS ProjectId,
                   is_active AS IsActive, created_at AS CreatedAt, revoked_at AS RevokedAt
            FROM morphdb._morph_secrets
            WHERE secret_hash = @SecretHash AND is_active = true
            """,
            new { SecretHash = Hash(presented) });
    }

    /// <summary>
    /// Hashes a plaintext to lowercase hex SHA-256.
    /// </summary>
    /// <remarks>
    /// A plain digest, not a password KDF, and deliberately so: these are 256-bit values this
    /// service generated itself, so there is no dictionary to slow an attacker down through. The
    /// reason to stretch a password -- that humans choose from a small space -- does not apply, and
    /// paying a KDF on every request would only make authentication a rate limiter on itself.
    /// </remarks>
    public static string Hash(string plaintext) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext)));

    private static string GeneratePlaintext() =>
        Prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(EntropyBytes));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
}
