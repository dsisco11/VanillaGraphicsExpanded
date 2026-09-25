using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Controlled source voxels published by the production shared partition and GPU backend.</summary>
internal sealed class SharedTraceGeometryFixture : IDisposable
{
    private readonly TraceGeometryPartition partition;
    private readonly PartitionCoordinator coordinator = new(new(16384, 256, 128, 128, 8 * 1024 * 1024));
    private int version;
    private long tick;
    public TraceGeometryGpuScene Scene { get; }
    public TraceGeometryCoverage Plan { get; private set; }
    public Func<int, int, int, TraceGeometryVoxel> Sample { get; set; }

    #region Fixture lifecycle and publication
    /// <summary>Injects synthetic source data while retaining real coherence, ring addressing and lease authorization.</summary>
    public SharedTraceGeometryFixture(TraceGeometryCoverage plan, TraceGeometryMaterials materials, Func<int, int, int, TraceGeometryVoxel> sample)
    {
        Plan = plan; Sample = sample; Scene = new(plan.Resolution);
        var cache = new TraceGeometrySourceCache((key, revision, _) =>
        {
            key.Decode(out int cx, out int cy, out int cz);
            var data = new TraceGeometryVoxel[32768];
            for (int y = 0; y < 32; y++) for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
                data[(y * 32 + z) * 32 + x] = Sample(cx * 32 + x, cy * 32 + y, cz * 32 + z);
            return Task.FromResult<TraceGeometryChunk?>(new(key, revision, data));
        }, _ => version, _ => true);
        partition = new(coordinator, cache, materials, Scene);
        partition.Prepare(plan);
    }

    /// <summary>Settles bounded work without replacing production admission decisions.</summary>
    public void Publish(int frames = 40)
    {
        for (int i = 0; i < frames; i++)
        { partition.Prepare(Plan); coordinator.Pump(++tick); partition.Service(Plan); }
    }

    /// <summary>Rejects existing contents before a replacement source revision is captured.</summary>
    public void Dirty() { version++; partition.Prepare(Plan); }

    /// <summary>Changes consumer coverage while preserving overlap and retiring departing owners.</summary>
    public void Move(TraceGeometryCoverage plan) { Plan = plan; partition.Prepare(plan); }

    /// <summary>Reads one published cell to distinguish source/publication errors from consumer sampling errors.</summary>
    public uint ReadGeometry(int x, int y, int z)
    {
        int n = Scene.Resolution;
        var words = new uint[n * n * n];
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, Scene.Geometry.TextureId);
        GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, words);
        return words[(((z % n + n) % n) * n + (y % n + n) % n) * n + (x % n + n) % n];
    }

    /// <summary>Retires the registration and its GPU storage.</summary>
    public void Dispose() => partition.Dispose();
    #endregion
}
