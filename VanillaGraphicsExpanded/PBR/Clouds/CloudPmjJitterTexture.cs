using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns cloud sampling independently of LumOn while reusing its scrambled progressive sequence generator.</summary>
internal sealed class CloudPmjJitterTexture : GpuTexture
{
    internal const int CycleLength = 128;
    private readonly PmjCache cache = new();

    #region Public API
    /// <summary>Allocates and uploads one deterministic cloud-owned sequence for immediate tracing.</summary>
    internal static CloudPmjJitterTexture Create()
    {
        var texture = new CloudPmjJitterTexture();
        try { texture.EnsureCreated(); return texture; }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>Reuses valid storage while rejecting attempts to revive a disposed resource owner.</summary>
    internal void EnsureCreated()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (IsValid) return;
        // Match LumOn's sequence construction, but give clouds an independent seed and GPU lifetime.
        var config = new PmjConfig(SampleCount: CycleLength, Seed: 0x434C4F55u,
            Variant: PmjVariant.Pmj02, OutputKind: PmjOutputKind.Vector2F32,
            OwenScramble: true, Salt: 0u, Centered: false);
        width = CycleLength; height = depth = 1;
        internalFormat = PixelInternalFormat.Rg16;
        textureTarget = TextureTarget.Texture2D;
        filterMode = TextureFilterMode.Nearest;
        debugName = "Cloud.PMJ.Jitter";
        AllocateOrReallocate2DTexture(mipLevels: 1);
        UploadDataImmediate(PmjConversions.ToRg16UNormInterleaved(cache.GetOrCreateSequence(config)));
    }
    #endregion

    #region Private
    /// <summary>Restricts construction to the factory which guarantees a complete initial upload.</summary>
    private CloudPmjJitterTexture() { }
    #endregion
}
