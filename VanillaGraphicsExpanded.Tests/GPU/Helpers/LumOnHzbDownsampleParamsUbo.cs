using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// CPU-side params for VgeLumOnHzbDownsampleParamsUBO (std140, 16 bytes).
/// The production shader exposes <c>srcMip</c> via a macro backed by this UBO.
/// </summary>
internal sealed class LumOnHzbDownsampleParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnHzbDownsampleParamsUBO";
    public const int UboSizeBytes = 16;

    public LumOnHzbDownsampleParamsUbo() : base(UboSizeBytes)
    {
    }

    public int SrcMip
    {
        set
        {
            UboPacking.WriteIVec4(DataWritable, 0, value, 0, 0, 0);
            MarkDirty(0, 16);
        }
    }
}
