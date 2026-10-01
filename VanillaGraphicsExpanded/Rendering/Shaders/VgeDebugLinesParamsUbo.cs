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
            WriteMatrix4(0, value);
        }
    }

    public Vintagestory.API.MathTools.Vec3f WorldOffset
    {
        set
        {
            WriteVector4(64, new(value.X, value.Y, value.Z, 0f));
        }
    }
}
