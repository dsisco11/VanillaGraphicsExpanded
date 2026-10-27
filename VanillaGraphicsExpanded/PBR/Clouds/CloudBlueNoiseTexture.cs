using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns spatial rank masks and their constrained cyclic refresh-phase labels.</summary>
internal sealed class CloudBlueNoiseTexture : GpuTexture
{
    internal const int Side = 32;
    internal const int Frames = 8;
    private static readonly Lazy<float[]> Masks = new(CreateMasks);

    #region Public API
    /// <summary>Creates complete cloud-owned sampling storage without borrowing another renderer's lifetime.</summary>
    internal static CloudBlueNoiseTexture Create()
    {
        var texture = new CloudBlueNoiseTexture();
        try { texture.EnsureCreated(); return texture; }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>Reuses live storage and uploads immutable CPU masks only when GPU storage is absent.</summary>
    internal void EnsureCreated()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (IsValid) return;
        filterMode = TextureFilterMode.Nearest;
        debugName = "Cloud.Primary.BlueNoise";
        AllocateOrReallocate2DTexture(Side, Side * Frames, TextureFormat.R32f, mipLevels: 1);
        UploadDataImmediate(Masks.Value);
    }
    #endregion

    #region Private
    /// <summary>Restricts construction to the exception-safe upload factory.</summary>
    private CloudBlueNoiseTexture() { }

    /// <summary>Builds tileable spatial masks and deterministically relabels their periodic sweep phases once.</summary>
    private static float[] CreateMasks()
    {
        var data = new float[Side * Side * Frames];
        for (int frame = 0; frame < Frames; frame++)
        {
            // Independent seeds retain spatial void-and-cluster structure in each temporal slice.
            var config = new BlueNoiseConfig(Side, Side, Seed: 0x434C4F55u + (uint)frame);
            var mask = VoidAndClusterGenerator.GenerateRankMap(config);
            BlueNoiseConversions.ToNormalizedF32(mask).CopyTo(data, frame * Side * Side);
        }
        return CloudRefreshSchedule.Create(data,Side,Frames);
    }
    #endregion
}
