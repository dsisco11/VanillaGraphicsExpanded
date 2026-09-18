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

    public LumOnWorldProbeResolveParamsUbo() : base(UboSizeBytes)
    {
    }

    public Vec2f AtlasSize
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, 0, value.X, value.Y, 0f, 0f);
            MarkDirty(0, UboSizeBytes);
        }
    }
}