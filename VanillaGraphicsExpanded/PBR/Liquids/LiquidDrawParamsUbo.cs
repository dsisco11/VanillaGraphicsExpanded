using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs the pool transform and origin into immutable per-submission ring allocations.</summary>
internal sealed class LiquidDrawParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidDrawParams";

    private const int ModelOffset = 0;
    private const int OriginOffset = 64;
    private const int TransparencyOffset = 76;

    #region Public API
    /// <summary>Allocates a std140 matrix followed by origin and preview transparency.</summary>
    internal LiquidDrawParamsUbo() : base(80, Rendering.Uniforms.UniformBufferUsage.SingleFrame)
    { WriteMatrix4(ModelOffset, Matrix4x4.Identity); }

    /// <summary>Extracts only the object transform from the engine combined camera/object matrix.</summary>
    internal void SetModelView(float[] matrix, VgeFrameUniformBuffer camera)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Length != 16) throw new ArgumentException("Expected a 4x4 matrix.", nameof(matrix));
        // Ordinary pools and the engine restoration write share the exact camera matrix.
        // Preserve an exact identity instead of introducing inverse-multiply roundoff each frame.
        if (matrix.AsSpan().SequenceEqual(camera.View))
        {
            ResetModelTransform();
            return;
        }
        Span<float> model = stackalloc float[16];
        MatrixHelper.Multiply(camera.InverseView, matrix, model);
        WriteMatrix4(ModelOffset, model);
    }

    /// <summary>Starts an ordinary pool sequence without retaining a prior mini-dimension transform.</summary>
    internal void ResetModelTransform() => WriteMatrix4(ModelOffset, Matrix4x4.Identity);

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
