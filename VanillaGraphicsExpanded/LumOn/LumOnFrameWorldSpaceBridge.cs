using System;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn;

internal static class LumOnFrameWorldSpaceBridge
{
    private const double ChunkSize = 32.0;

    public static (VectorInt3 ChunkOffset, Vector3d BlockOffsetRemainder) Compute(
        double cameraWorldX,
        double cameraWorldY,
        double cameraWorldZ,
        ReadOnlySpan<float> invViewMatrix)
    {
        double offsetX = cameraWorldX - invViewMatrix[12];
        double offsetY = cameraWorldY - invViewMatrix[13];
        double offsetZ = cameraWorldZ - invViewMatrix[14];

        int chunkOffsetX = (int)Math.Floor(offsetX / ChunkSize);
        int chunkOffsetY = (int)Math.Floor(offsetY / ChunkSize);
        int chunkOffsetZ = (int)Math.Floor(offsetZ / ChunkSize);

        return (
            new VectorInt3(chunkOffsetX, chunkOffsetY, chunkOffsetZ),
            new Vector3d(
                offsetX - (chunkOffsetX * ChunkSize),
                offsetY - (chunkOffsetY * ChunkSize),
                offsetZ - (chunkOffsetZ * ChunkSize)));
    }
}
