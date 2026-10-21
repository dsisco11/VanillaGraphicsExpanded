using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Noise;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Cached Sobol-based jitter points in a small RG16 texture, independently owned by each rendering consumer.
/// Inherits from <see cref="GpuTexture"/> to provide RAII + deferred disposal.
/// </summary>
public sealed class PmjJitterTexture : GpuTexture
{
    private readonly PmjCache cache = new();
    private readonly PmjConfig config;

    #region Public API
    /// <summary>Reports the immutable point count for modulo indexing.</summary>
    public int CycleLength => config.SampleCount;
    /// <summary>Allocates and uploads the immutable sequence through the ordinary GPU texture owner.</summary>
    public static PmjJitterTexture Create(int cycleLength, uint seed)
    {
        var texture = new PmjJitterTexture(cycleLength, seed);
        try { texture.EnsureCreated(); return texture; }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>Recreates storage after context retirement; ordinary frames retain the existing upload.</summary>
    public void EnsureCreated()
    {
        ObjectDisposedException.ThrowIf(IsDisposed,this);
        if (IsValid)
        {
            return;
        }

        // RG16_UNorm 1xN "1D" texture stored as 2D.
        width = CycleLength;
        height = 1;
        depth = 1;
        internalFormat = PixelInternalFormat.Rg16;
        textureTarget = TextureTarget.Texture2D;
        filterMode = TextureFilterMode.Nearest;
        debugName = "Sampling.PMJ.Jitter";

        AllocateOrReallocate2DTexture(mipLevels: 1);

        PmjSequence seq = cache.GetOrCreateSequence(config);
        ushort[] rg = PmjConversions.ToRg16UNormInterleaved(seq);
        // This texture is sampled immediately by GPU tests and the renderer; make upload synchronous.
        UploadDataImmediate(rg);
    }
    #endregion
    #region Private
    /// <summary>Freezes sequence identity without depending on an effect configuration or renderer.</summary>
    private PmjJitterTexture(int cycleLength, uint seed)
    {
        if (cycleLength <= 0) throw new ArgumentOutOfRangeException(nameof(cycleLength));
        config = new PmjConfig(SampleCount:cycleLength,Seed:seed,Variant:PmjVariant.Pmj02,
            OutputKind:PmjOutputKind.Vector2F32,OwenScramble:true,Salt:0u,Centered:false);
    }
    #endregion
}
