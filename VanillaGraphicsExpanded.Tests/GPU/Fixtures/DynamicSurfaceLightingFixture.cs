using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns a controlled shared geometry scene, captured page and production irradiance-history invalidation.</summary>
internal sealed class DynamicSurfaceLightingFixture : IDisposable
{
    private readonly ScopedPbrMaterialFixture material = new();
    private readonly BinaryShaderApiFixture assets = new();
    public uint MaterialId { get; }
    public SharedTraceGeometryFixture Geometry { get; }
    public SharedSurfacePageFixture Page { get; }
    public LumonSceneIrradianceHistory History { get; }

    #region Controlled scene lifetime
    /// <summary>Publishes a uniformly illuminated solid scene whose page rays resolve real geometry in every direction.</summary>
    public DynamicSurfaceLightingFixture(int initialLight)
        : this((id, _, _, _) => new(2u | id << 2, LumonSceneOccupancyPacking.PackClamped(initialLight, 0, 0, (int)id), 0)) { }

    /// <summary>Publishes an authored voxel scene while retaining real materials, capture and history ownership.</summary>
    public DynamicSurfaceLightingFixture(Func<uint, int, int, int, TraceGeometryVoxel> sample)
    {
        material.SetReadiness(true, true);
        var materials = new TraceGeometryMaterials();
        MaterialId = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(0, 32, 0), true, 32, 256);
        Geometry = new(plan, materials, (x, y, z) => sample(MaterialId, x, y, z));
        Geometry.Publish();
        Page = new();
        History = new(assets.Api);
    }

    /// <summary>Releases the history owner before its atlas and restores the scoped material registry.</summary>
    public void Dispose()
    {
        History.Dispose(); Page.Dispose(); Geometry.Dispose(); assets.Dispose(); material.Dispose();
    }
    #endregion
}
