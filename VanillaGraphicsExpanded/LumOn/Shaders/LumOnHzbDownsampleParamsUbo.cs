using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnHzbDownsampleParamsUBO (std140, 16 bytes).
/// </summary>
public sealed class LumOnHzbDownsampleParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnHzbDownsampleParamsUBO";
    public const int UboSizeBytes = 16;

    public LumOnHzbDownsampleParamsUbo() : base(UboSizeBytes)
    {
    }

    public int SrcMip
    {
        get => UboPacking.ReadInt32(DataReadOnly, 0);
        set
        {
            UboPacking.WriteIVec4(DataWritable, 0, value, 0, 0, 0);
            MarkDirty(0, UboSizeBytes);
        }
    }
}