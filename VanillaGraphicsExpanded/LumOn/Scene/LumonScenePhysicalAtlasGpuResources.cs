using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal sealed class LumonScenePhysicalAtlasGpuResources : IDisposable
{
    private readonly int atlasCount;
    private readonly int tileSizeTexels;

    private readonly Texture3D depthAtlas;
    private readonly Texture3D materialAtlas;
    private readonly Texture3D irradianceAtlas;

    public int AtlasCount => atlasCount;
    public int TileSizeTexels => tileSizeTexels;

    public Texture3D DepthAtlas => depthAtlas;
    public Texture3D MaterialAtlas => materialAtlas;
    public Texture3D IrradianceAtlas => irradianceAtlas;

    public int DepthAtlasTextureId => depthAtlas.TextureId;
    public int MaterialAtlasTextureId => materialAtlas.TextureId;
    public int IrradianceAtlasTextureId => irradianceAtlas.TextureId;

    public LumonScenePhysicalAtlasGpuResources(LumonSceneField field, int atlasCount, int tileSizeTexels)
    {
        if (atlasCount <= 0) throw new ArgumentOutOfRangeException(nameof(atlasCount));
        if (tileSizeTexels <= 0) throw new ArgumentOutOfRangeException(nameof(tileSizeTexels));

        this.atlasCount = atlasCount;
        this.tileSizeTexels = tileSizeTexels;

        // v1 formats (subject to change):
        // - Depth: R16F displacement (or 0 for planar)
        // - Material: RGBA8 placeholder (later: pack normal + material ids)
        // - Irradiance: RGBA16F (RGB=irradiance, A=weight/age)
        string prefix = field == LumonSceneField.Near ? "LumonScene_Near" : "LumonScene_Far";

        depthAtlas = Texture3D.Create(
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels,
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels,
            atlasCount,
            PixelInternalFormat.R16f,
            TextureFilterMode.Nearest,
            TextureTarget.Texture2DArray,
            $"{prefix}_DepthAtlas");

        materialAtlas = Texture3D.Create(
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels,
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels,
            atlasCount,
            PixelInternalFormat.Rgba8,
            TextureFilterMode.Nearest,
            TextureTarget.Texture2DArray,
            $"{prefix}_MaterialAtlas");

        irradianceAtlas = Texture3D.Create(
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels,
            LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels,
            atlasCount,
            PixelInternalFormat.Rgba16f,
            TextureFilterMode.Nearest,
            TextureTarget.Texture2DArray,
            $"{prefix}_IrradianceAtlas");

        Label(depthAtlas);
        Label(materialAtlas);
        Label(irradianceAtlas);
    }

    public void Dispose()
    {
        depthAtlas.Dispose();
        materialAtlas.Dispose();
        irradianceAtlas.Dispose();
    }

    private static void Label(GpuTexture texture)
    {
        if (texture.TextureId == 0) return;
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, texture.TextureId, texture.DebugName);
    }
}

