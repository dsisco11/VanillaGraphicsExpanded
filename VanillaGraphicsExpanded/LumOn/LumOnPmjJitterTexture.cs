using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

internal sealed class LumOnPmjJitterTexture : IDisposable
{
    private readonly PmjCache cache = new();
    private readonly PmjConfig config;

    private DynamicTexture2D? texture;

    public int TextureId => texture?.TextureId ?? 0;
    public int CycleLength => config.SampleCount;

    public LumOnPmjJitterTexture(int cycleLength, uint seed)
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

    public void EnsureCreated()
    {
        if (texture is not null)
        {
            return;
        }

        // RG16_UNorm 1xN “1D” texture stored as 2D.
        texture = DynamicTexture2D.Create(CycleLength, 1, PixelInternalFormat.Rg16, TextureFilterMode.Nearest, debugName: "LumOn.PMJ.Jitter");

        PmjSequence seq = cache.GetOrCreateSequence(config);
        ushort[] rg = PmjConversions.ToRg16UNormInterleaved(seq);
        texture.UploadData(rg);
    }

    public void Dispose()
    {
        texture?.Dispose();
        texture = null;
    }
}
