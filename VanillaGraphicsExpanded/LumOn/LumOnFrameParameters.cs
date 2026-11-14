using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns LumOn's effect controls and retained compatibility illumination layout.</summary>
internal sealed class LumOnFrameParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 112;
    internal const int ExtentOffset = 0;
    internal const int ProbeOffset = 16;
    internal const int ReprojectionOffset = 32;
    internal const int JitterOffset = 48;
    internal const int DirectionOffset = 64;
    internal const int DirectOffset = 80;
    internal const int EnvironmentOffset = 96;

    /// <summary>Declares the complete effect-control block independently of camera storage.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages the existing controls and already-selected compatibility light without changing calibration.</summary>
    internal void Set(float halfWidth, float halfHeight, float gridWidth, float gridHeight, int spacing,
        int historyValid, int jitterEnabled, int cycleLength, int reprojectVelocity,
        float jitterScale, float velocityThreshold, Vector3 direction, Vector3 direct, Vector3 environment)
    {
        WriteVector4(ExtentOffset, new(halfWidth, halfHeight, gridWidth, gridHeight));
        WriteIntVector4(ProbeOffset, spacing, 0, historyValid, jitterEnabled);
        WriteIntVector4(ReprojectionOffset, cycleLength, reprojectVelocity, 0, 0);
        WriteVector4(JitterOffset, new(jitterScale, velocityThreshold, 0, 0));
        WriteVector4(DirectionOffset, new(direction, 0));
        WriteVector4(DirectOffset, new(direct, 0));
        WriteVector4(EnvironmentOffset, new(environment, 0));
    }
    #endregion
}
