using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Render-thread access to a coherent surface-lighting generation; no GPU readback is performed.</summary>
internal interface ISurfaceLightingProvider
{
    /// <summary>Returns the current lighting generation only while its dependency identity remains valid.</summary>
    bool TryGetSurfaceLighting(out SurfaceLightingSnapshot snapshot);
}

/// <summary>Borrowed resources for immediate render-thread use. Page readiness admits verified captured pages with some initialized lighting; outgoing alpha validates each texel. LightingIsStale reports changed source inputs without invalidating retained values. Direct and indirect atlases are progressive diagnostic layers. The owner retains disposal rights.</summary>
internal readonly record struct SurfaceLightingSnapshot(
    Texture3D OutgoingRadiance, Texture3D DirectIrradiance, Texture3D IndirectIrradiance,
    Texture3D PageTable, Texture3D Material, GpuShaderStorageBuffer Patches,
    GpuShaderStorageBuffer Slots, GpuShaderStorageBuffer Readiness,
    VectorInt3 Origin, VectorInt3 Dimensions, VectorInt3 Ring,
    int TileSize, int TilesPerAxis, int TilesPerAtlas, long Generation, long DependencyRevision = 0,
    bool LightingIsStale = false);
