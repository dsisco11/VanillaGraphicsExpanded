using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Local trace origin, resolution and traversal budget; 32-byte std140 contract.</summary>
internal sealed class LumOnLocalTraceParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "LumOnLocalTraceUBO";
    public const int Binding = GpuBindingRegistry.Ubo.Material;

    /// <summary>Allocates parameters disabled until a published scene is bound.</summary>
    public LumOnLocalTraceParamsUbo() : base(32) { }

    /// <summary>Writes all mapping parameters together to avoid mixed publication generations.</summary>
    public void Set(VectorInt3 origin, int resolution, int maxSteps = 256)
    {
        UboPacking.WriteIVec4(DataWritable, 0, origin.X, origin.Y, origin.Z, resolution);
        UboPacking.WriteIVec4(DataWritable, 16, maxSteps, 0, 0, 0);
        MarkDirty(0, 32);
    }
}
