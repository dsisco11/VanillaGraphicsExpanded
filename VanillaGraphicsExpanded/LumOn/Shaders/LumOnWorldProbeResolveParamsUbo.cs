using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnWorldProbeResolveParamsUBO (std140, 16 bytes).
/// </summary>
public sealed class LumOnWorldProbeResolveParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnWorldProbeResolveParamsUBO";
    public const int UboSizeBytes = 16;

    /// <summary>Retains atlas dimensions across frames until the owning resolve target changes.</summary>
    public LumOnWorldProbeResolveParamsUbo() : base(UboSizeBytes, Rendering.Uniforms.UniformBufferUsage.MultiFrame)
    {
    }

    public Vec2f AtlasSize
    {
        set
        {
            WriteVector4(0, new(value.X, value.Y, 0f, 0f));
        }
    }
}
