using System;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Converts engine render-relative positions to absolute world cells with a precise origin split.</summary>
internal static class LumOnFrameWorldSpaceBridge
{
    private const double ChunkSize = 32.0;

    #region World Origin
    /// <summary>Splits the CameraPos origin used by terrain rendering into chunks and a bounded remainder.</summary>
    public static (VectorInt3 ChunkOffset, Vector3d BlockOffsetRemainder) Compute(
        double renderOriginX,
        double renderOriginY,
        double renderOriginZ)
    {
        // ChunkRenderer subtracts CameraPos before applying the view matrix. Inverting that
        // matrix recovers render-relative coordinates; add CameraPos without subtracting
        // inverse-view translation a second time. Callers must pass the same render origin.
        int chunkOffsetX = (int)Math.Floor(renderOriginX / ChunkSize);
        int chunkOffsetY = (int)Math.Floor(renderOriginY / ChunkSize);
        int chunkOffsetZ = (int)Math.Floor(renderOriginZ / ChunkSize);

        // Subtract in double precision before the UBO converts the small remainder to float.
        return (
            new VectorInt3(chunkOffsetX, chunkOffsetY, chunkOffsetZ),
            new Vector3d(
                renderOriginX - (chunkOffsetX * ChunkSize),
                renderOriginY - (chunkOffsetY * ChunkSize),
                renderOriginZ - (chunkOffsetZ * ChunkSize)));
    }
    #endregion
}
