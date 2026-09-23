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
            Depth = Texture3D.Create(width,height,layers,PixelInternalFormat.R16f,TextureFilterMode.Nearest,TextureTarget.Texture2DArray,name+"_DepthAtlas");
            Material = Texture3D.Create(width,height,layers,PixelInternalFormat.Rgba8,TextureFilterMode.Nearest,TextureTarget.Texture2DArray,name+"_MaterialAtlas");
            Indirect = Texture3D.Create(width,height,layers,PixelInternalFormat.Rgba16f,TextureFilterMode.Nearest,TextureTarget.Texture2DArray,name+"_IrradianceAtlas");
            Direct = Texture3D.Create(width,height,layers,PixelInternalFormat.Rgba16f,TextureFilterMode.Nearest,TextureTarget.Texture2DArray,name+"_DirectIrradiance");
            for (int i=0;i<Outgoing.Length;i++)
                Outgoing[i] = Texture3D.Create(width,height,layers,PixelInternalFormat.Rgba16f,TextureFilterMode.Nearest,TextureTarget.Texture2DArray,name+"_OutgoingRadiance"+i);
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
