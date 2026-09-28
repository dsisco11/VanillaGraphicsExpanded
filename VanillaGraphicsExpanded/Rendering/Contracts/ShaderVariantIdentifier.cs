using System;
using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Produces compact, case-independent filenames from canonical shader variant keys.</summary>
internal static class ShaderVariantIdentifier
{
    #region Identifier encoding
    /// <summary>Encodes the first 128 SHA-256 bits as 26 unpadded RFC 4648 Base32 characters.</summary>
    internal static string Create(string key)
    {
        ReadOnlySpan<byte> hash = SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        Span<char> encoded = stackalloc char[26];
        uint accumulator = 0;
        int bits = 0, position = 0;
        // Emit five-bit groups in network order; pad the final three data bits with two zero bits.
        foreach (byte value in hash)
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
