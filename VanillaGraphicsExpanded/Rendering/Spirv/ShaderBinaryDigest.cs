using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Defines the build-generated digest manifest shared by the compiler and runtime.</summary>
internal static class ShaderBinaryDigest
{
    internal const string FileName = "spirv-digests.json";
    /// <summary>Identifies the size and SHA-256 digest of one compiled variant.</summary>
    internal sealed record Entry(int Length, string Digest);
    /// <summary>Maps shader-relative binary paths to their build-generated identities.</summary>
    internal sealed record Manifest(int Version, Dictionary<string, Entry> Binaries);

    #region Metadata format
    /// <summary>Serializes all variants in stable path order for deterministic build output.</summary>
    internal static byte[] Encode(Dictionary<string, Entry> entries) => JsonSerializer.SerializeToUtf8Bytes(
        new Manifest(1, entries.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));

    /// <summary>Rejects malformed or unsupported manifests without preventing ordinary shader loading.</summary>
    internal static Manifest? Parse(ReadOnlySpan<byte> metadata)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(metadata);
            return manifest?.Version == 1 && manifest.Binaries != null ? manifest : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Validates the requested entry and decodes its precomputed hash without hashing SPIR-V.</summary>
    internal static bool TryRead(Manifest? manifest, string binaryPath, int binaryLength, out byte[] digest)
    {
        digest = [];
        if (manifest == null || !manifest.Binaries.TryGetValue(binaryPath, out var entry) || entry == null ||
            binaryLength <= 0 || entry.Length != binaryLength || entry.Digest?.Length != 64) return false;
        try { digest = Convert.FromHexString(entry.Digest); return true; }
        catch (FormatException) { return false; }
    }
    #endregion
}
