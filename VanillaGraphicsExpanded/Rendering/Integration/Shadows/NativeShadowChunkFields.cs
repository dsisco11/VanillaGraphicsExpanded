using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Caches access to the supported engine's chunk-owned range arrays and work flags.</summary>
internal static class NativeShadowChunkFields
{
    internal static readonly AccessTools.FieldRef<ClientChunk, ModelDataPoolLocation[]> Centers =
        AccessTools.FieldRefAccess<ClientChunk, ModelDataPoolLocation[]>("centerModelPoolLocations");
    internal static readonly AccessTools.FieldRef<ClientChunk, ModelDataPoolLocation[]> Edges =
        AccessTools.FieldRefAccess<ClientChunk, ModelDataPoolLocation[]>("edgeModelPoolLocations");
    internal static readonly AccessTools.FieldRef<ClientChunk, bool> QueuedUpload =
        AccessTools.FieldRefAccess<ClientChunk, bool>("queuedForUpload");
    internal static readonly AccessTools.FieldRef<ClientChunk, bool> QueuedRedraw =
        AccessTools.FieldRefAccess<ClientChunk, bool>("enquedForRedraw");
}
