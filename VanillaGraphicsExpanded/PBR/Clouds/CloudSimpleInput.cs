using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Native geometry borrowed only for synchronous submission inside the owning callback; the native renderer retains disposal authority.</summary>
internal readonly record struct CloudSimpleInput(object Owner, MeshRef Mesh, int Count, int TileSize, int Width,
    CloudPlacement Placement, long World, uint Frame);
