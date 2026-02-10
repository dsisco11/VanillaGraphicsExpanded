using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// CPU-side params for VgeLumOnWorldProbeResolveParamsUBO (std140, 16 bytes).
/// The production vertex shader exposes <c>atlasSize</c> via a macro backed by this UBO.
/// </summary>
internal sealed class LumOnWorldProbeResolveParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnWorldProbeResolveParamsUBO";
    public const int UboSizeBytes = 16;

    public LumOnWorldProbeResolveParamsUbo() : base(UboSizeBytes)
    {
    }

    public Vector2 AtlasSize
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, 0, value.X, value.Y, 0f, 0f);
            MarkDirty(0, 16);
        }
    }
}
