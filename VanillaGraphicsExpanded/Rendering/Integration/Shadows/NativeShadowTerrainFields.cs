using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Reads the native category table and storage owners without recording reverse pool membership.</summary>
internal static class NativeShadowTerrainFields
{
    internal static readonly AccessTools.FieldRef<ChunkRenderer, ClientPlatformAbstract> Platform =
        AccessTools.FieldRefAccess<ChunkRenderer, ClientPlatformAbstract>("platform");
    internal static readonly AccessTools.FieldRef<ChunkRenderer, MeshDataPoolManager[][]> Table =
        AccessTools.FieldRefAccess<ChunkRenderer, MeshDataPoolManager[][]>("poolsByRenderPass");
    internal static readonly AccessTools.FieldRef<ChunkRenderer, int[]> Textures =
        AccessTools.FieldRefAccess<ChunkRenderer, int[]>("textureIds");
    internal static readonly AccessTools.FieldRef<MeshDataPoolManager, List<MeshDataPool>> Pools =
        AccessTools.FieldRefAccess<MeshDataPoolManager, List<MeshDataPool>>("pools");
    internal static readonly AccessTools.FieldRef<MeshDataPool, MeshRef> Mesh =
        AccessTools.FieldRefAccess<MeshDataPool, MeshRef>("modelRef");
    internal static readonly AccessTools.FieldRef<MeshDataPool, int> Id =
        AccessTools.FieldRefAccess<MeshDataPool, int>("poolId");
    internal static readonly AccessTools.FieldRef<MeshDataPool, int> Dimension =
        AccessTools.FieldRefAccess<MeshDataPool, int>("dimensionId");
    internal static readonly AccessTools.FieldRef<MeshDataPool, Vintagestory.API.MathTools.Vec3i> Origin =
        AccessTools.FieldRefAccess<MeshDataPool, Vintagestory.API.MathTools.Vec3i>("poolOrigin");
}
