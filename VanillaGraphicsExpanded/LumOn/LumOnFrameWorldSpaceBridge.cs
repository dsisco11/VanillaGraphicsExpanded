using System;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Converts player-relative render positions to absolute world cells without camera motion.</summary>
internal static class LumOnFrameWorldSpaceBridge
{
    private const double ChunkSize = 32.0;

    #region World Origin
    /// <summary>Splits the stable player origin into integer chunks and a bounded fractional block remainder.</summary>
    public static (VectorInt3 ChunkOffset, Vector3d BlockOffsetRemainder) Compute(
        double playerOriginX,
        double playerOriginY,
        double playerOriginZ)
    {
        // Inverse-view reconstruction already includes camera bob and yields player-relative
        // coordinates. Only the player origin belongs in this bridge; subtracting view
        // translation again would move stationary geometry with the camera.
        int chunkOffsetX = (int)Math.Floor(playerOriginX / ChunkSize);
        int chunkOffsetY = (int)Math.Floor(playerOriginY / ChunkSize);
        int chunkOffsetZ = (int)Math.Floor(playerOriginZ / ChunkSize);

        // Subtract in double precision before the UBO converts the small remainder to float.
        return (
            new VectorInt3(chunkOffsetX, chunkOffsetY, chunkOffsetZ),
            new Vector3d(
                playerOriginX - (chunkOffsetX * ChunkSize),
                playerOriginY - (chunkOffsetY * ChunkSize),
                playerOriginZ - (chunkOffsetZ * ChunkSize)));
    }
    #endregion
}
