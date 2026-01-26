using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Progressive Multi-Jittered (PMJ) jitter texture for LumOn ray tracing.
/// Inherits from <see cref="GpuTexture"/> to provide RAII + deferred disposal.
/// </summary>
public sealed class LumOnPmjJitterTexture : GpuTexture
{
    private readonly PmjCache cache = new();
    private readonly PmjConfig config;

    public int CycleLength => config.SampleCount;

    private LumOnPmjJitterTexture(int cycleLength, uint seed)
    {
        if (cycleLength <= 0) throw new ArgumentOutOfRangeException(nameof(cycleLength));

        config = new PmjConfig(
            SampleCount: cycleLength,
            Seed: seed,
            Variant: PmjVariant.Pmj02,
            OutputKind: PmjOutputKind.Vector2F32,
            OwenScramble: true,
            Salt: 0u,
            Centered: false);
    }

    public static LumOnPmjJitterTexture Create(int cycleLength, uint seed)
    {
        var texture = new LumOnPmjJitterTexture(cycleLength, seed);
        texture.EnsureCreated();
        return texture;
    }

    public void EnsureCreated()
    {
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
        debugName = "LumOn.PMJ.Jitter";

        AllocateOrReallocate2DTexture(mipLevels: 1);

        PmjSequence seq = cache.GetOrCreateSequence(config);
        ushort[] rg = PmjConversions.ToRg16UNormInterleaved(seq);
        // This texture is sampled immediately by GPU tests and the renderer; make upload synchronous.
        UploadDataImmediate(rg);
    }

}
