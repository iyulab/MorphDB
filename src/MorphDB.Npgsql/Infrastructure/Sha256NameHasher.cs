using System.Security.Cryptography;
using System.Text;
using MorphDB.Core.Abstractions;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// SHA256-based name hasher for generating physical names.
/// </summary>
public sealed class Sha256NameHasher : INameHasher
{
    private const int MaxPostgresIdentifierLength = 63;
    private const int HashLength = 12; // Truncated hash for readability

    public string GenerateTableName(Guid tenantId, string logicalName)
    {
        var input = $"{tenantId}:table:{logicalName}";
        var hash = ComputeHash(input);
        return $"tbl_{hash}";
    }

    public string GenerateColumnName(Guid tableId, string logicalName)
    {
        var input = $"{tableId}:column:{logicalName}";
        var hash = ComputeHash(input);
        return $"col_{hash}";
    }

    public string GenerateIndexName(Guid tableId, string logicalName)
    {
        var input = $"{tableId}:index:{logicalName}";
        var hash = ComputeHash(input);
        return $"idx_{hash}";
    }

    public string GenerateConstraintName(string prefix, Guid tableId, string logicalName)
    {
        var input = $"{tableId}:{prefix}:{logicalName}";
        var hash = ComputeHash(input);
        return $"{prefix}_{hash}";
    }

    public bool IsValidPhysicalName(string name)
    {
        return !string.IsNullOrEmpty(name) && name.Length <= MaxPostgresIdentifierLength;
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        var fullHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return fullHash[..HashLength];
    }
}
