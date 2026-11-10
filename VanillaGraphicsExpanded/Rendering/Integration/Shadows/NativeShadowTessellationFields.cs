using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Caches native upload metadata access without retaining or classifying mesh pools.</summary>
internal static class NativeShadowTessellationFields
{
    internal static readonly AccessTools.FieldRef<TesselatedChunk, int> X = AccessTools.FieldRefAccess<TesselatedChunk, int>("positionX");
    internal static readonly AccessTools.FieldRef<TesselatedChunk, int> Y = AccessTools.FieldRefAccess<TesselatedChunk, int>("positionYAndDimension");
    internal static readonly AccessTools.FieldRef<TesselatedChunk, int> Z = AccessTools.FieldRefAccess<TesselatedChunk, int>("positionZ");
    internal static readonly AccessTools.FieldRef<TesselatedChunk, ClientChunk> Chunk = AccessTools.FieldRefAccess<TesselatedChunk, ClientChunk>("chunk");
    internal static readonly AccessTools.FieldRef<TesselatedChunk, TesselatedChunkPart[]> Centers =
        AccessTools.FieldRefAccess<TesselatedChunk, TesselatedChunkPart[]>("centerParts");
    internal static readonly AccessTools.FieldRef<TesselatedChunk, TesselatedChunkPart[]> Edges =
        AccessTools.FieldRefAccess<TesselatedChunk, TesselatedChunkPart[]>("edgeParts");
    internal static readonly AccessTools.FieldRef<TesselatedChunkPart, MeshData> Lod0 = AccessTools.FieldRefAccess<TesselatedChunkPart, MeshData>("modelDataLod0");
    internal static readonly AccessTools.FieldRef<TesselatedChunkPart, MeshData> Lod1 = AccessTools.FieldRefAccess<TesselatedChunkPart, MeshData>("modelDataLod1");
    internal static readonly AccessTools.FieldRef<TesselatedChunkPart, MeshData> Near2 = AccessTools.FieldRefAccess<TesselatedChunkPart, MeshData>("modelDataNotLod2Far");
    internal static readonly AccessTools.FieldRef<TesselatedChunkPart, MeshData> Far2 = AccessTools.FieldRefAccess<TesselatedChunkPart, MeshData>("modelDataLod2Far");
}
