using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>A dark enclosed room separated from constantly lit exterior cells by a closable voxel doorway.</summary>
internal sealed class ControlledSurfaceDoorwayScene
{
    public bool DoorOpen { get; set; } = true;

    #region Voxel source
    /// <summary>Changes only doorway occupancy; interior and exterior lighting remain fixed across every transition.</summary>
    public TraceGeometryVoxel Sample(uint materialId, int x, int y, int z)
    {
        // The captured +X patch is the back wall at x=1, y=32..36, z=0..4.
        // Side walls and the far wall enclose every deterministic hemisphere ray.
        bool shell = x <= 0 || x >= 8 || y <= 31 || y >= 37 || z <= -1 || z >= 5;
        bool aperture = y >= 32 && y <= 35 && z >= 0 && z <= 3;
        bool occupied = shell || (x == 4 && (!aperture || !DoorOpen));
        uint geometry = occupied ? 2u | materialId << 2 : 1u;
        uint lighting = LumonSceneOccupancyPacking.PackClamped(x >= 5 ? 32 : 0, 0, 0, (int)materialId);
        return new(geometry, lighting, 0);
    }
    #endregion
}
