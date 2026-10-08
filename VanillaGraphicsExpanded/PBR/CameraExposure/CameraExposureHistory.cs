using System;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Detects discontinuities without reading temporal exposure back from the GPU.</summary>
internal sealed class CameraExposureHistory
{
    private CameraExposureParameters? previousSettings;
    private double x, y, z;
    private float yaw, pitch;
    private int dimension, cameraMode;
    #region Public API
    /// <summary>Requests a fresh measurement on startup, settings changes, long gaps, teleports and camera cuts.</summary>
    internal bool Capture(CameraExposureParameters settings, float deltaTime, double nextX, double nextY,
        double nextZ, float nextYaw, float nextPitch, int nextDimension, int nextCameraMode)
    {
        double dx = nextX - x, dy = nextY - y, dz = nextZ - z;
        // Ordinary movement and looking retain adaptation. Large discontinuities have no useful history.
        float turn = MathF.Abs(MathF.IEEERemainder(nextYaw - yaw, 2 * MathF.PI));
        bool reset = previousSettings != settings || !float.IsFinite(deltaTime) || deltaTime < 0 || deltaTime > 1
            || dx * dx + dy * dy + dz * dz > 32 * 32 || turn > MathF.PI * .75f
            || MathF.Abs(nextPitch - pitch) > MathF.PI * .5f
            || dimension != nextDimension || cameraMode != nextCameraMode;
        previousSettings = settings;
        (x, y, z, yaw, pitch, dimension, cameraMode) = (nextX, nextY, nextZ, nextYaw, nextPitch, nextDimension, nextCameraMode);
        return reset;
    }
    /// <summary>Invalidates history when its image storage or world is retired.</summary>
    internal void Reset() { previousSettings = null; }
    #endregion
}
