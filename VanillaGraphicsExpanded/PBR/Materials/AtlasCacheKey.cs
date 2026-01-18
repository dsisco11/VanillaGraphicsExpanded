using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Versioned cache key used to identify cached atlas build outputs.
/// The key is intentionally schema-versioned so future contract changes can invalidate older cache entries.
/// </summary>
internal readonly record struct AtlasCacheKey(int SchemaVersion, ulong Hash64)
{
    public override string ToString()
        => $"v{SchemaVersion}:{Hash64:x16}";

    public static AtlasCacheKey FromUtf8(int schemaVersion, string stableKey)
    {
        ArgumentNullException.ThrowIfNull(stableKey);

        byte[] bytes = Encoding.UTF8.GetBytes(stableKey);
        byte[] digest = SHA256.HashData(bytes);
        ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(digest);
        return new AtlasCacheKey(schemaVersion, hash);
    }
}
