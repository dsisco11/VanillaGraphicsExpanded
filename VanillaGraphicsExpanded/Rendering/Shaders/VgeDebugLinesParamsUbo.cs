using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// CPU-side wrapper for VgeDebugLinesParamsUBO (std140, 80 bytes).
/// </summary>
public sealed class VgeDebugLinesParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeDebugLinesParamsUBO";
    public const int UboSizeBytes = 80;

    public VgeDebugLinesParamsUbo() : base(UboSizeBytes)
    {
    }

    public float[] ModelViewProjectionMatrix
    {
        set
        {
            UboPacking.WriteMat4(DataWritable, 0, value);
            MarkDirty(0, 64);
        }
    }

    public Vintagestory.API.MathTools.Vec3f WorldOffset
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, 64, value.X, value.Y, value.Z, 0f);
            MarkDirty(64, 16);
        }
    }
}