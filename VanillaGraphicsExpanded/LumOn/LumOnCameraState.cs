using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Captures the world and camera positions needed by LumOn render scheduling.</summary>
internal readonly record struct LumOnCameraState(
    double PositionX,
    double PositionY,
    double PositionZ,
    double CameraX,
    double CameraY,
    double CameraZ,
    int Dimension)
{
    /// <summary>Reads the current player entity without retaining it across frames or world changes.</summary>
    public static LumOnCameraState? Read(ICoreClientAPI api)
    {
        var entity = api.World?.Player?.Entity;
        return entity is null
            ? null
            : new(entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
                entity.CameraPos.X, entity.CameraPos.Y, entity.CameraPos.Z, entity.Pos.Dimension);
    }
}
