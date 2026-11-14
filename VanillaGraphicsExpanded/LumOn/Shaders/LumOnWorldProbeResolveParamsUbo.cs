using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnWorldProbeResolveParamsUBO (std140, 16 bytes).
/// </summary>
public sealed class LumOnWorldProbeResolveParamsUbo : CpuUniformBuffer
{
    #region Public API
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => UboSizeBytes;

    public const string BlockName = "VgeLumOnWorldProbeResolveParamsUBO";
    public const int UboSizeBytes = 16;

    /// <summary>Retains atlas dimensions across frames until the owning resolve target changes.</summary>
    public LumOnWorldProbeResolveParamsUbo() : base(Rendering.Uniforms.UniformBufferUsage.MultiFrame)
    {
    }

    public Vec2f AtlasSize
    {
        set
        {
            WriteVector4(0, new(value.X, value.Y, 0f, 0f));
        }
    }
    #endregion
}
