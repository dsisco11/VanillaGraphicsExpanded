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

    #region Public API
    #region Layout
    internal const int MappingOffset = 0;
    internal const int StepCellOffset = 16;
    internal const int SupportedBoundsMinOffset = 32;
    internal const int SupportedBoundsMaxOffset = 48;
    internal const int NearFieldDomainOffset = 64;
    internal const int NearFieldDomainMaxOffset = 80;
    internal const int SurfaceDomainOffset = 96;
    internal const int SurfaceDomainMaxOffset = 112;
    internal const int DomainBoundsStride = 16;
    #endregion

    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 128;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates parameters disabled until a published scene is bound.</summary>
    public LumOnNearFieldParamsUbo() : base() { }

    /// <summary>Writes coherent mapping and optional origin bounds in small relative coordinates.</summary>
    public void Set(in VectorInt3 origin, int resolution, int maxSteps = 256, int cellSize = 16,
        PartitionBounds? supportedOrigins = null, float maximumTraceReach = 0)
    {
        WriteMapping(origin, resolution, maxSteps, cellSize, supportedOrigins, maximumTraceReach);
        var domain = new PartitionBounds(new(origin.X, origin.Y, origin.Z), new(origin.X + resolution, origin.Y + resolution, origin.Z + resolution));
        WriteDomain(NearFieldDomainOffset, resolution > 0 ? domain : null);
        WriteDomain(SurfaceDomainOffset, resolution > 0 ? domain : null);

    }

    /// <summary>Writes shared allocation mapping while keeping each consumer's logical bounds independent.</summary>
    public void SetShared(TraceGeometryGpuScene? scene, LumOnNearFieldTraceSettings? settings = null)
    {
        var plan = scene?.Coverage;
        var min = plan?.Window.Min ?? default;
        WriteMapping(new((int)min.X, (int)min.Y, (int)min.Z), scene?.Resolution ?? 0, settings?.MaxSteps ?? 256, 16,
            supportedOrigins: settings is null ? plan?.NearField : settings.SupportedOrigins,
            maximumTraceReach: settings?.MaximumTraceReach ?? float.MaxValue);
        WriteDomain(NearFieldDomainOffset, plan?.NearField);
        WriteDomain(SurfaceDomainOffset, plan?.Surface);

    }

    #endregion

    #region Private
    /// <summary>Updates physical mapping without temporarily overwriting independent consumer domains.</summary>
    private void WriteMapping(in VectorInt3 origin, int resolution, int maxSteps, int cellSize,
        PartitionBounds? supportedOrigins, float maximumTraceReach)
    {
        WriteIntVector4(MappingOffset, origin.X, origin.Y, origin.Z, resolution);
        WriteIntVector4(StepCellOffset, maxSteps, cellSize, supportedOrigins.HasValue ? 1 : 0, 0);
        PartitionBounds bounds = supportedOrigins ?? default;
        WriteVector4(SupportedBoundsMinOffset, new((float)(bounds.Min.X - origin.X), (float)(bounds.Min.Y - origin.Y), (float)(bounds.Min.Z - origin.Z), maximumTraceReach));
        WriteVector4(SupportedBoundsMaxOffset, new((float)(bounds.Max.X - origin.X), (float)(bounds.Max.Y - origin.Y), (float)(bounds.Max.Z - origin.Z), 0));
    }

    /// <summary>Encodes an optional half-open logical domain without floating-point world coordinates.</summary>
    private void WriteDomain(int offset, PartitionBounds? domain)
    {
        var bounds = domain ?? default;
        WriteIntVector4(offset, (int)bounds.Min.X, (int)bounds.Min.Y, (int)bounds.Min.Z, domain.HasValue ? 1 : 0);
        WriteIntVector4(offset + DomainBoundsStride, (int)bounds.Max.X, (int)bounds.Max.Y, (int)bounds.Max.Z, 0);
    }
    #endregion
}
