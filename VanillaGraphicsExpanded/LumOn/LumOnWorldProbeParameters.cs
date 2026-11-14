using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the world-probe fallback illumination and fixed clipmap-placement arrays.</summary>
internal sealed class LumOnWorldProbeParameters : CpuUniformBuffer
{
    #region Public API
    internal const int MaximumLevels = 8;
    internal const int VectorStride = 16;
    internal const int EnvironmentOffset = 0;
    internal const int CameraPositionOffset = 16;
    internal const int OriginsOffset = 32;
    internal const int RingsOffset = OriginsOffset + MaximumLevels * VectorStride;
    internal const int BlockSize = RingsOffset + MaximumLevels * VectorStride;

    /// <summary>Declares the header and both complete std140 placement arrays.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages current placement, clearing unused array entries rather than retaining an older level.</summary>
    internal void Set(Vector3 environment, Vector3 cameraPosition,
        ReadOnlySpan<Vector3> origins, ReadOnlySpan<Vector3> rings)
    {
        WriteVector4(EnvironmentOffset, new(environment, 0));
        WriteVector4(CameraPositionOffset, new(cameraPosition, 0));
        // Visit both complete arrays so a shorter publication cannot retain older clipmap levels.
        for (int level = 0; level < MaximumLevels; level++)
            WriteVector4(OriginsOffset + level * VectorStride, new(level < origins.Length ? origins[level] : default, 0));
        for (int level = 0; level < MaximumLevels; level++)
            WriteVector4(RingsOffset + level * VectorStride, new(level < rings.Length ? rings[level] : default, 0));
    }
    #endregion
}
