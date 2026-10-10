using System;
using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Produces compact, case-independent filenames from canonical shader variant keys.</summary>
internal static class ShaderVariantIdentifier
{
    #region Public API
    /// <summary>Encodes the first 128 SHA-256 bits as 26 unpadded RFC 4648 Base32 characters.</summary>
    internal static string Create(string key)
    {
        return Encode(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    }

    /// <summary>Encodes bytes as uppercase, unpadded RFC 4648 Base32 for case-insensitive filenames.</summary>
    internal static string Encode(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        Span<char> encoded = bytes.Length <= 128 ? stackalloc char[(bytes.Length * 8 + 4) / 5] : new char[checked((bytes.Length * 8 + 4) / 5)];
        uint accumulator = 0;
        int bits = 0, position = 0;
        // Emit five-bit groups in network order; zero-pad any remaining data bits.
        foreach (byte value in bytes)
        {
            accumulator = (accumulator << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                encoded[position++] = alphabet[(int)((accumulator >> bits) & 31)];
            }
        }
        if (bits != 0) encoded[position] = alphabet[(int)((accumulator << (5 - bits)) & 31)];
        return new string(encoded);
    }
    #endregion
}
