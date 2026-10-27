using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns the material and lighting textures that share one physical surface-atlas layout.</summary>
internal sealed class SurfaceAtlasTextures : IDisposable
{
    public Texture3D Depth { get; }
    public Texture3D Material { get; }
    public Texture3D Indirect { get; }
    public Texture3D Direct { get; }
    public Texture3D[] Outgoing { get; } = new Texture3D[2];

    #region Allocation
    /// <summary>Allocates a coherent layer set; residency and total-byte admission remain with the physical atlas owner.</summary>
    public SurfaceAtlasTextures(int width, int height, int layers, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(layers);
        try
        {
            Depth = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, layers), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name+"_DepthAtlas" }).RequireResource();
            Material = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, layers), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name+"_MaterialAtlas" }).RequireResource();
            Indirect = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, layers), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name+"_IrradianceAtlas" }).RequireResource();
            Direct = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, layers), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name+"_DirectIrradiance" }).RequireResource();
            for (int i=0;i<Outgoing.Length;i++)
                Outgoing[i] = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, layers), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name+"_OutgoingRadiance"+i }).RequireResource();
        }
        catch { Dispose(); throw; }
    }
    #endregion

    #region Lifetime
    /// <summary>Releases all companion layers, including a partially completed allocation.</summary>
    public void Dispose()
    {
        Depth?.Dispose(); Material?.Dispose(); Indirect?.Dispose(); Direct?.Dispose();
        foreach (var texture in Outgoing) texture?.Dispose();
    }
    #endregion
}
