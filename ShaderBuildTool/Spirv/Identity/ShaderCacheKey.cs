using System.Security.Cryptography;
using System.Text.Json;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Produces full-strength SHA-256 cache references using the shared filename-safe Base32 encoding.</summary>
internal static class ShaderCacheKey
{
    #region Public API
    /// <summary>Frames identity components unambiguously before generating a compact cache filename.</summary>
    internal static string Create(params string[] parts) => Content(JsonSerializer.SerializeToUtf8Bytes(parts));

    /// <summary>Encodes all 256 digest bits as 52 uppercase, unpadded Base32 characters.</summary>
    internal static string Content(byte[] bytes) => ShaderVariantIdentifier.Encode(SHA256.HashData(bytes));

    /// <summary>Rejects paths, noncanonical alphabet and nonzero padding bits before cache access.</summary>
    internal static bool IsValid(string? key) => key is { Length: 52 }
        && key.All(c => c is >= 'A' and <= 'Z' or >= '2' and <= '7')
        && key[^1] is 'A' or 'Q';
    #endregion
}
