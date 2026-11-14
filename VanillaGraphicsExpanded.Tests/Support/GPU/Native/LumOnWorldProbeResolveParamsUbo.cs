using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Native;

/// <summary>
/// CPU-side params for VgeLumOnWorldProbeResolveParamsUBO (std140, 16 bytes).
/// The production vertex shader exposes <c>atlasSize</c> via a macro backed by this UBO.
/// </summary>
internal sealed class LumOnWorldProbeResolveParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnWorldProbeResolveParamsUBO";
    public const int UboSizeBytes = 16;
    /// <summary>Reports the fixed std140 block size used to allocate the CPU storage.</summary>
    public override int SizeBytes => UboSizeBytes;

    public LumOnWorldProbeResolveParamsUbo() : base()
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
