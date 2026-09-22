using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Shared physical ring and independent consumer domains; 128-byte std140 contract.</summary>
internal sealed class LumOnNearFieldParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "LumOnNearFieldUBO";
    public const int Binding = GpuBindingRegistry.Ubo.Material;

    /// <summary>Allocates parameters disabled until a published scene is bound.</summary>
    public LumOnNearFieldParamsUbo() : base(128) { }

    /// <summary>Writes coherent mapping and optional origin bounds in small relative coordinates.</summary>
    public void Set(in VectorInt3 origin, int resolution, int maxSteps = 256, int cellSize = 16,
        PartitionBounds? supportedOrigins = null, float maximumTraceReach = 0)
    {
        UboPacking.WriteIVec4(DataWritable, 0, origin.X, origin.Y, origin.Z, resolution);
        UboPacking.WriteIVec4(DataWritable, 16, maxSteps, cellSize, supportedOrigins.HasValue ? 1 : 0, 0);
        PartitionBounds bounds = supportedOrigins ?? default;
        UboPacking.WriteVec4(DataWritable, 32, (float)(bounds.Min.X - origin.X), (float)(bounds.Min.Y - origin.Y), (float)(bounds.Min.Z - origin.Z), maximumTraceReach);
        UboPacking.WriteVec4(DataWritable, 48, (float)(bounds.Max.X - origin.X), (float)(bounds.Max.Y - origin.Y), (float)(bounds.Max.Z - origin.Z), 0);
        var domain = new PartitionBounds(new(origin.X, origin.Y, origin.Z), new(origin.X + resolution, origin.Y + resolution, origin.Z + resolution));
        WriteDomain(64, resolution > 0 ? domain : null);
        WriteDomain(96, resolution > 0 ? domain : null);
        MarkDirty(0, 128);
    }

    /// <summary>Writes shared allocation mapping while keeping each consumer's logical bounds independent.</summary>
    public void SetShared(TraceGeometryGpuScene? scene, int maxSteps = 256)
    {
        var plan = scene?.Coverage;
        var min = plan?.Window.Min ?? default;
        Set(new((int)min.X, (int)min.Y, (int)min.Z), scene?.Resolution ?? 0, maxSteps,
            supportedOrigins: plan?.NearField, maximumTraceReach: float.MaxValue);
        WriteDomain(64, plan?.NearField);
        WriteDomain(96, plan?.Surface);
        MarkDirty(0, 128);
    }

    /// <summary>Encodes an optional half-open logical domain without floating-point world coordinates.</summary>
    private void WriteDomain(int offset, PartitionBounds? domain)
    {
        var bounds = domain ?? default;
        UboPacking.WriteIVec4(DataWritable, offset, (int)bounds.Min.X, (int)bounds.Min.Y, (int)bounds.Min.Z, domain.HasValue ? 1 : 0);
        UboPacking.WriteIVec4(DataWritable, offset + 16, (int)bounds.Max.X, (int)bounds.Max.Y, (int)bounds.Max.Z, 0);
    }
}
