using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes reusable controlled voxel arrangements through the production GPU scene owner.</summary>
internal sealed class LocalTraceVoxelFixture : IDisposable
{
    public LocalTraceGpuScene Scene { get; } = new(64);
    public LumonSceneTraceSceneChunkVersionProvider Versions { get; } = new();
    private readonly LocalTraceMaterialRegistry materials = new();

    #region Fixture Lifecycle
    /// <summary>Initializes a known bounded scene centered at the supplied integer world position.</summary>
    public LocalTraceVoxelFixture(VectorInt3 center = default) => Scene.Prepare(center, Versions);

    /// <summary>Releases the production textures owned by this fixture.</summary>
    public void Dispose() => Scene.Dispose();
    #endregion

    #region Publication
    /// <summary>Converts controlled cells to source artifacts, retaining world identities and X/Z/Y source ordering.</summary>
    public void Publish(ControlledVoxelWorld world, Vector4? material = null, uint materialIdentity = 1)
    {
        var origin = Scene.Origin;
        for (int rz = 0; rz < Scene.RegionResolution; rz++)
        for (int ry = 0; ry < Scene.RegionResolution; ry++)
        for (int rx = 0; rx < Scene.RegionResolution; rx++)
        {
            var coordinate = new VectorInt3((origin.X >> 5) + rx, (origin.Y >> 5) + ry, (origin.Z >> 5) + rz);
            var key = ChunkKey.FromChunkCoords(coordinate.X, coordinate.Y, coordinate.Z);
            var cells = new LocalTraceSourceCell[32768];
            for (int y = 0; y < 32; y++)
            for (int z = 0; z < 32; z++)
            for (int x = 0; x < 32; x++)
            {
                var position = (coordinate.X * 32 + x, coordinate.Y * 32 + y, coordinate.Z * 32 + z);
                uint geometry = !world.IsLoaded(position) ? 0u : world.GetBlock(position).BlockId == 0 ? 1u : 2u | (materialIdentity << 2);
                cells[(y * 32 + z) * 32 + x] = new LocalTraceSourceCell(geometry, world.GetLight(position));
            }
            Scene.Publish(new LumonSceneTraceSceneRegionArtifact(key, Versions.GetCurrentVersion(key), coordinate, new uint[32768])
                { LocalCells = cells }, materials, Versions);
        }
        // Material identity 1 is deliberately independent of the game registry.
        var value = material ?? new Vector4(1, 1, 1, 0);
        var data = new float[LocalTraceMaterialRegistry.Width * LocalTraceMaterialRegistry.Height * 4];
        for (int face = 0; face < 6; face++)
        {
            int i = (12 + face * 2) * 4;
            data[i] = value.X; data[i + 1] = value.Y; data[i + 2] = value.Z; data[i + 3] = 0;
            data[i + 4] = value.X * value.W; data[i + 5] = value.Y * value.W; data[i + 6] = value.Z * value.W;
        }
        Scene.Materials.UploadDataImmediate(data);
    }
    #endregion
}
