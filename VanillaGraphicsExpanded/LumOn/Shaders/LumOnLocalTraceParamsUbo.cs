using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Local ring mapping and supported origin domain; 64-byte std140 contract.</summary>
internal sealed class LumOnLocalTraceParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "LumOnLocalTraceUBO";
    public const int Binding = GpuBindingRegistry.Ubo.Material;

    /// <summary>Allocates parameters disabled until a published scene is bound.</summary>
    public LumOnLocalTraceParamsUbo() : base(64) { }

    /// <summary>Writes coherent mapping and optional origin bounds in small relative coordinates.</summary>
    public void Set(in VectorInt3 origin, int resolution, int maxSteps = 256, int cellSize = 16,
        PartitionBounds? supportedOrigins = null, float maximumTraceReach = 0)
    {
        UboPacking.WriteIVec4(DataWritable, 0, origin.X, origin.Y, origin.Z, resolution);
        UboPacking.WriteIVec4(DataWritable, 16, maxSteps, cellSize, supportedOrigins.HasValue ? 1 : 0, 0);
        PartitionBounds bounds = supportedOrigins ?? default;
        UboPacking.WriteVec4(DataWritable, 32, (float)(bounds.Min.X - origin.X), (float)(bounds.Min.Y - origin.Y), (float)(bounds.Min.Z - origin.Z), maximumTraceReach);
        UboPacking.WriteVec4(DataWritable, 48, (float)(bounds.Max.X - origin.X), (float)(bounds.Max.Y - origin.Y), (float)(bounds.Max.Z - origin.Z), 0);
        MarkDirty(0, 64);
    }
}
