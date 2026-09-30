using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs the pool transform and origin into immutable per-submission ring allocations.</summary>
internal sealed class LiquidDrawParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidDrawParams";

    private const int ModelViewOffset = 0;
    private const int OriginOffset = 64;
    private const int TransparencyOffset = 76;

    #region Draw parameters
    /// <summary>Allocates a std140 matrix followed by origin and preview transparency.</summary>
    internal LiquidDrawParamsUbo() : base(80) { }

    /// <summary>Copies engine column-major storage without performing engine matrix arithmetic.</summary>
    internal void SetModelView(float[] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Length != 16) throw new ArgumentException("Expected a 4x4 matrix.", nameof(matrix));
        WriteMatrix4(ModelViewOffset, matrix);
    }

    /// <summary>Updates the camera-relative pool offset without replacing preview opacity.</summary>
    internal void SetOrigin(Vector3 origin)
    {
        WriteVector3(OriginOffset, origin);
    }

    /// <summary>Matches the engine preview convention, where zero means no forced transparency.</summary>
    internal void SetTransparency(float transparency)
    {
        WriteFloat(TransparencyOffset, Math.Clamp(transparency, 0, 1));
    }
    #endregion
}
