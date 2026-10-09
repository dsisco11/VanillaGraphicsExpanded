using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Identifies camera discontinuities independently of any effect's settings or GPU history.</summary>
internal sealed class FrameCameraContinuity
{
    private bool captured;
    private double x, y, z;
    private float yaw, pitch;
    private int dimension, cameraMode;

    #region Public API
    /// <summary>Retains ordinary motion while rejecting startup, long gaps, teleports and abrupt view changes.</summary>
    internal bool Capture(float deltaTime, double nextX, double nextY, double nextZ,
        float nextYaw, float nextPitch, int nextDimension, int nextCameraMode)
    {
        double dx = nextX - x, dy = nextY - y, dz = nextZ - z;
        // Wrapped yaw avoids treating an ordinary crossing of the angular seam as a camera cut.
        float turn = MathF.Abs(MathF.IEEERemainder(nextYaw - yaw, 2 * MathF.PI));
        bool cut = !captured || !float.IsFinite(deltaTime) || deltaTime < 0 || deltaTime > 1
            || dx * dx + dy * dy + dz * dz > 32 * 32 || turn > MathF.PI * .75f
            || MathF.Abs(nextPitch - pitch) > MathF.PI * .5f
            || dimension != nextDimension || cameraMode != nextCameraMode;
        (x, y, z, yaw, pitch, dimension, cameraMode) =
            (nextX, nextY, nextZ, nextYaw, nextPitch, nextDimension, nextCameraMode);
        captured = true;
        return cut;
    }

    /// <summary>Withdraws continuity when the owning view or world is retired.</summary>
    internal void Reset() => captured = false;
    #endregion
}
