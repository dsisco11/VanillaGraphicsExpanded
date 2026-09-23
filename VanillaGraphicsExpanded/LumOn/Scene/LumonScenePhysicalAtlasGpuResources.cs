using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns bounded material and lighting layers sharing one physical page layout.</summary>
internal sealed class LumonScenePhysicalAtlasGpuResources : IDisposable
{
    private readonly int atlasCount;
    private readonly int tileSizeTexels;

    private readonly Texture3D depthAtlas;
    private readonly Texture3D materialAtlas;
    private readonly Texture3D irradianceAtlas;
    private readonly Texture3D directAtlas;
    private readonly Texture3D[] outgoing;
    private readonly long allocationBytes;
    private bool disposed;
    private int publishedIndex;
    /// <summary>Bounds all live surface atlases, including overlapping recreation allocations.</summary>
    internal const long TotalByteBudget = 256L * 1024 * 1024;
    internal const int BytesPerTexel = 38;
    private static long allocatedBytes;
    #region Allocation and publication
    /// <summary>Checks allocation admission on the render thread before runtime creation.</summary>
    internal static bool CanAllocate(int layers) => System.Threading.Interlocked.Read(ref allocatedBytes) +
        (long)layers * LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * BytesPerTexel <= TotalByteBudget;
    public Texture3D DirectAtlas => directAtlas;
    public Texture3D PublishedOutgoing => outgoing[publishedIndex];
    public Texture3D PendingOutgoing => outgoing[1 - publishedIndex];
    public long LightingGeneration { get; private set; }

    /// <summary>Switches the outgoing generation after complete tile writes and barriers.</summary>
    public void Publish() { publishedIndex = 1 - publishedIndex; LightingGeneration++; }

    public int AtlasCount => atlasCount;
    public int TileSizeTexels => tileSizeTexels;

    public Texture3D DepthAtlas => depthAtlas;
    public Texture3D MaterialAtlas => materialAtlas;
    public Texture3D IrradianceAtlas => irradianceAtlas;

    public int DepthAtlasTextureId => depthAtlas.TextureId;
    public int MaterialAtlasTextureId => materialAtlas.TextureId;
    public int IrradianceAtlasTextureId => irradianceAtlas.TextureId;

    /// <summary>Reserves total byte credit before allocating any companion layer.</summary>
    public LumonScenePhysicalAtlasGpuResources(LumonSceneField field, int atlasCount, int tileSizeTexels)
    {
        if (atlasCount <= 0) throw new ArgumentOutOfRangeException(nameof(atlasCount));
        if (tileSizeTexels <= 0) throw new ArgumentOutOfRangeException(nameof(tileSizeTexels));

        allocationBytes = checked((long)LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels * atlasCount * BytesPerTexel);
        if (System.Threading.Interlocked.Add(ref allocatedBytes, allocationBytes) > TotalByteBudget)
        {
            System.Threading.Interlocked.Add(ref allocatedBytes, -allocationBytes);
            throw new InvalidOperationException("Surface atlas total byte budget exhausted.");
        }
        try
        {
            this.atlasCount = atlasCount;
            this.tileSizeTexels = tileSizeTexels;

            // Shared page storage: depth, captured face identity, and four half-float lighting layers.
            // - Depth: R16F displacement (or 0 for planar)
            // - Material: RGBA8 (oct normal and 16-bit immutable surface identity)
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

            directAtlas = Texture3D.Create(depthAtlas.Width, depthAtlas.Height, atlasCount, PixelInternalFormat.Rgba16f,
                TextureFilterMode.Nearest, TextureTarget.Texture2DArray, $"{prefix}_DirectIrradiance");
            outgoing = new Texture3D[2];
            for (int i = 0; i < outgoing.Length; i++)
                outgoing[i] = Texture3D.Create(depthAtlas.Width, depthAtlas.Height, atlasCount, PixelInternalFormat.Rgba16f,
                    TextureFilterMode.Nearest, TextureTarget.Texture2DArray, $"{prefix}_OutgoingRadiance{i}");
            Label(depthAtlas);
            Label(materialAtlas);
            Label(irradianceAtlas);
            }
        catch
        {
            depthAtlas?.Dispose(); materialAtlas?.Dispose(); irradianceAtlas?.Dispose(); directAtlas?.Dispose();
            if (outgoing != null) foreach (var texture in outgoing) texture?.Dispose();
            System.Threading.Interlocked.Add(ref allocatedBytes, -allocationBytes);
            throw;
        }
    }

    #endregion

    #region Resource lifetime
    /// <summary>Retires all layers before returning allocation credit.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        directAtlas.Dispose(); foreach (var texture in outgoing) texture.Dispose();
        depthAtlas.Dispose();
        materialAtlas.Dispose();
        irradianceAtlas.Dispose();
        // Disposal/recreation is a cold lifecycle path. Finish submitted readers before
        // returning byte credit, so deferred GL deletion cannot hide recreation overlap.
        GL.Finish();
        System.Threading.Interlocked.Add(ref allocatedBytes, -allocationBytes);
    }

    /// <summary>Labels a live GL allocation for diagnostics.</summary>
    private static void Label(GpuTexture texture)
    {
        if (texture.TextureId == 0) return;
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, texture.TextureId, texture.DebugName);
    }
    #endregion
}
