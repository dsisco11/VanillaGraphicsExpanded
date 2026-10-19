using System;
using System.IO;
using System.Security.Cryptography;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Validates the immutable interleaved octave volume before any GPU resource is admitted.</summary>
internal sealed class CloudPerturbationAsset
{
    internal const int Side = 64;
    internal const int PayloadBytes = Side * Side * Side * 4;
    internal const string Digest = "7B2D9323D65E4E93F3A5DCC85F5C412D0CBC174E10EA3F0A102135B259C313F8";
    private readonly byte[] pixels;

    #region Public API
    /// <summary>Exposes validated upload bytes without generating noise in the runtime.</summary>
    internal byte[] Pixels => pixels;
    /// <summary>Decodes only the supported fixed layout; malformed or truncated assets fail before publication.</summary>
    internal CloudPerturbationAsset(Stream source)
    {
        using var reader = new BinaryReader(source, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != 0x4E434756u || reader.ReadInt32() != 1 || reader.ReadInt32() != Side
            || reader.ReadInt32() != 4 || reader.ReadUInt32() != 0x434C4F55u || reader.ReadInt32() != 8
            || reader.ReadInt32() != 1 || reader.ReadInt32() != 0)
            throw new InvalidDataException("Unsupported cloud perturbation asset.");
        pixels = reader.ReadBytes(PayloadBytes);
        if (pixels.Length != PayloadBytes || source.ReadByte() != -1)
            throw new InvalidDataException("Invalid cloud perturbation payload length.");
        if (Convert.ToHexString(SHA256.HashData(pixels)) != "B500321B64838A4484DF9BCB683E47F7E25B294932455A0D72FCBDCF89AD74BE")
            throw new InvalidDataException("Cloud perturbation payload identity mismatch.");
    }
    /// <summary>Loads the assembly-owned asset once per requesting resource lifetime, with no per-frame I/O.</summary>
    internal static CloudPerturbationAsset Load()
    {
        using var source = typeof(CloudPerturbationAsset).Assembly.GetManifestResourceStream("VGE.CloudPerturbation")
            ?? throw new InvalidDataException("Cloud perturbation asset is missing.");
        return new(source);
    }
    #endregion
}
