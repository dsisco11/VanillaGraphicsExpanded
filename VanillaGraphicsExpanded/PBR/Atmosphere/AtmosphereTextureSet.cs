using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns one coherently uploaded sky and finite-path texture generation.</summary>
internal sealed class AtmosphereTextureSet : IDisposable
{
    internal DynamicTexture2D Sky { get; }
    internal DynamicTexture3D Radiance { get; }
    internal DynamicTexture3D Attenuation { get; }

    #region Construction and upload
    /// <summary>Allocates the full generation, or neutral one-voxel transport for the startup snapshot.</summary>
    internal AtmosphereTextureSet(AtmosphereLighting lighting)
    {
        int depth = lighting.AerialRadiance.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth;
        int width = depth == 1 ? 1 : lighting.Width, height = depth == 1 ? 1 : lighting.Height;
        Sky = DynamicTexture2D.Create(lighting.Width, lighting.Height, PixelInternalFormat.Rgba16f, debugName: "Atmosphere.Sky");
        try
        {
            Radiance = DynamicTexture3D.Create(width, height, depth, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
            Attenuation = DynamicTexture3D.Create(width, height, depth, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
            foreach (GpuTexture texture in new GpuTexture[] { Sky, Radiance, Attenuation })
            {
                texture.DisableMipmaps();
                texture.SetTexFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
                texture.SetTexWrap(TextureWrapMode.Repeat, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            }
        }
        catch { Sky.Dispose(); Radiance?.Dispose(); Attenuation?.Dispose(); throw; }
    }

    /// <summary>Checks storage compatibility without changing the currently published set.</summary>
    internal bool Matches(AtmosphereLighting lighting) => Sky.Width == lighting.Width && Sky.Height == lighting.Height
        && Radiance.Depth == (lighting.AerialRadiance.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth);

    /// <summary>Uploads immutable completed data synchronously; callers publish only after all uploads succeed.</summary>
    internal void Upload(AtmosphereLighting lighting)
    {
        if (!lighting.AerialRadiance.IsEmpty && (lighting.AerialRadiance.Length != lighting.Width * lighting.Height * AtmosphereAerialPerspective.Depth * 4
            || lighting.AerialAttenuation.Length != lighting.AerialRadiance.Length))
            throw new ArgumentException("Atmospheric volume dimensions do not match the snapshot.", nameof(lighting));
        // The GPU upload only reads these arrays; no mutable reference escapes this owner.
        Sky.UploadData(ImmutableCollectionsMarshal.AsArray(lighting.Sky)!);
        float[] neutral = [0, 0, 0, 1];
        Radiance.UploadData(lighting.AerialRadiance.IsEmpty ? neutral : ImmutableCollectionsMarshal.AsArray(lighting.AerialRadiance)!,
            0, 0, 0, Radiance.Width, Radiance.Height, Radiance.Depth);
        Attenuation.UploadData(lighting.AerialRadiance.IsEmpty ? neutral : ImmutableCollectionsMarshal.AsArray(lighting.AerialAttenuation)!,
            0, 0, 0, Attenuation.Width, Attenuation.Height, Attenuation.Depth);
    }
    #endregion

    #region Lifetime
    /// <summary>Retires all resources in the set through their GPU owners.</summary>
    public void Dispose() { Sky.Dispose(); Radiance.Dispose(); Attenuation.Dispose(); }
    #endregion
}
