using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Bounded region-aligned GPU snapshot with explicit world-region identities and normalized lighting.</summary>
internal sealed class LocalTraceGpuScene : IDisposable
{
    private readonly (ChunkKey Key, int Version, bool Ready)[] published;
    public int Resolution { get; }
    public int RegionResolution => Resolution / 32;
    public Texture3D Geometry { get; }
    public Texture3D Light { get; }
    public Texture3D Regions { get; }
    public Texture2D Materials { get; }
    public long Revision { get; private set; }
    private VectorInt3 origin;
    public VectorInt3 Origin => origin;

    #region Lifetime
    /// <summary>Allocates bounded companion textures; only published regions may be sampled.</summary>
    public LocalTraceGpuScene(int requestedResolution)
    {
        Resolution = Math.Max(32, ((requestedResolution + 31) / 32) * 32);
        Geometry = Texture3D.Create(Resolution, Resolution, Resolution, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, debugName: "LumOn.LocalTrace.Geometry");
        Light = Texture3D.Create(Resolution, Resolution, Resolution, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, debugName: "LumOn.LocalTrace.Light");
        Regions = Texture3D.Create(RegionResolution, RegionResolution, RegionResolution, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest, debugName: "LumOn.LocalTrace.Regions");
        Materials = Texture2D.Create(LocalTraceMaterialRegistry.Width, LocalTraceMaterialRegistry.Height,
            PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, debugName: "LumOn.LocalTrace.Materials");
        published = new (ChunkKey, int, bool)[RegionResolution * RegionResolution * RegionResolution];
        Regions.UploadDataImmediate(new uint[published.Length * 4], 0, 0, 0, RegionResolution, RegionResolution, RegionResolution);
    }

    /// <summary>Releases the complete published scene.</summary>
    public void Dispose()
    {
        Geometry.Dispose(); Light.Dispose(); Regions.Dispose(); Materials.Dispose();
    }
    #endregion

    #region Publication
    /// <summary>Updates the bounded window and invalidates dirty regions before the Opaque consumer reads them.</summary>
    public void Prepare(VectorInt3 center, LumonSceneTraceSceneChunkVersionProvider versions)
    {
        var next = new VectorInt3(((center.X >> 5) - RegionResolution / 2) * 32,
            ((center.Y >> 5) - RegionResolution / 2) * 32, ((center.Z >> 5) - RegionResolution / 2) * 32);
        if (next != origin) { origin = next; Revision++; }
        for (int i = 0; i < published.Length; i++)
        {
            var region = published[i];
            if (region.Ready && versions.GetCurrentVersion(region.Key) != region.Version)
            {
                region.Key.Decode(out int x, out int y, out int z);
                Regions.UploadDataImmediate(new uint[4], Mod(x), Mod(y), Mod(z), 1, 1, 1);
                published[i] = (region.Key, region.Version, false);
                Revision++;
            }
        }
    }

    /// <summary>Tests region coverage without converting absolute world coordinates to floating point.</summary>
    public bool ContainsRegion(int regionX, int regionY, int regionZ)
        => (uint)(regionX * 32 - origin.X) < Resolution &&
           (uint)(regionY * 32 - origin.Y) < Resolution &&
           (uint)(regionZ * 32 - origin.Z) < Resolution;

    /// <summary>Publishes one complete region only after all lighting/material uploads and version checks succeed.</summary>
    public void Publish(LumonSceneTraceSceneRegionArtifact artifact, LocalTraceMaterialRegistry materials,
        LumonSceneTraceSceneChunkVersionProvider versions)
    {
        if (artifact.LocalCells is not { } cells || versions.GetCurrentVersion(artifact.Key) != artifact.Version) return;
        var rc = artifact.RegionCoord;
        // Reject work outside the consumer window so old completions cannot evict current slots.
        if (!ContainsRegion(rc.X, rc.Y, rc.Z)) return;
        int sx = Mod(rc.X), sy = Mod(rc.Y), sz = Mod(rc.Z);
        int slot = (sz * RegionResolution + sy) * RegionResolution + sx;
        Regions.UploadDataImmediate(new uint[4], sx, sy, sz, 1, 1, 1);
        published[slot] = (artifact.Key, artifact.Version, false);
        var geometry = new uint[32 * 32 * 32];
        var light = new float[geometry.Length * 4];
        // Snapshot order is X/Z/Y; texture uploads are X/Y/Z.
        for (int zz = 0; zz < 32; zz++)
        for (int yy = 0; yy < 32; yy++)
        for (int xx = 0; xx < 32; xx++)
        {
            var c = cells[(yy * 32 + zz) * 32 + xx];
            int dest = (zz * 32 + yy) * 32 + xx;
            geometry[dest] = c.Geometry;
            light[dest * 4] = c.Light.X; light[dest * 4 + 1] = c.Light.Y;
            light[dest * 4 + 2] = c.Light.Z; light[dest * 4 + 3] = c.Light.W;
        }
        Geometry.UploadDataImmediate(geometry, sx * 32, sy * 32, sz * 32, 32, 32, 32);
        Light.UploadDataImmediate(light, sx * 32, sy * 32, sz * 32, 32, 32, 32);
        if (materials.TakeUpload() is { } upload) Materials.UploadDataImmediate(upload);
        GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        if (versions.GetCurrentVersion(artifact.Key) != artifact.Version) return;
        Regions.UploadDataImmediate(new[] { unchecked((uint)rc.X), unchecked((uint)rc.Y), unchecked((uint)rc.Z), 1u }, sx, sy, sz, 1, 1, 1);
        published[slot] = (artifact.Key, artifact.Version, true);
        Revision++;
    }

    /// <summary>Maps signed world-region coordinates into bounded physical slots.</summary>
    private int Mod(int coordinate) => ((coordinate % RegionResolution) + RegionResolution) % RegionResolution;
    #endregion
}
