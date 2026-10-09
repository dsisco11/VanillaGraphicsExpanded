using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Detects discontinuities without reading temporal exposure back from the GPU.</summary>
internal sealed class CameraExposureHistory
{
    private CameraExposureParameters? previousSettings;
    private readonly FrameCameraContinuity continuity = new();
    #region Public API
    /// <summary>Requests a fresh measurement on startup, settings changes, long gaps, teleports and camera cuts.</summary>
    internal bool Capture(CameraExposureParameters settings, float deltaTime, double nextX, double nextY,
        double nextZ, float nextYaw, float nextPitch, int nextDimension, int nextCameraMode)
    {
        bool cameraCut = continuity.Capture(deltaTime, nextX, nextY,
            nextZ, nextYaw, nextPitch, nextDimension, nextCameraMode);
        bool reset = previousSettings != settings || cameraCut;
        previousSettings = settings;
        return reset;
    }
    /// <summary>Invalidates history when its image storage or world is retired.</summary>
    internal void Reset() { previousSettings = null; continuity.Reset(); }
    #endregion
}
