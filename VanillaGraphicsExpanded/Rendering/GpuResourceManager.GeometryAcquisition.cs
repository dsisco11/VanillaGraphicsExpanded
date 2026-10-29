using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Admits authored external geometry declarations without adding a canonical VAO layout mirror.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Validates authored streams and retains the original external configuration identity without scanning it.</summary>
    internal static RHIGeometryImport AcquireEngineGeometryConfiguration(GpuResource<GpuVertexArrayMetadata> array,
        RHIVertexLayout layout, IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices)
    {
        RequireWrapperMetadataAccess(array);
        ObjectDisposedException.ThrowIf(!array.IsValid, array);
        var execution = RHICommandContext.Current;
        var original = array.ResourceManager?.GetResourceRegistration(array);
        if (original is not null && array.ResourceManager?.IsResourceMetadataAvailable(array) != true)
            throw new InvalidOperationException("External vertex-array metadata is unavailable.");
        ulong revision = original?.MetadataRevision ?? 0;
        var before = execution.CaptureGeometryContext(checked((int)array.ResourceId));
        execution.ValidateImportedGeometry(checked((int)array.ResourceId), layout, streams, indices);
        var certification = execution.CaptureGeometryContext(checked((int)array.ResourceId));
        if (before != certification || revision != (original?.MetadataRevision ?? 0)
            || !array.IsValid || !ReferenceEquals(original, array.ResourceManager?.GetResourceRegistration(array))
            || original is not null && array.ResourceManager?.IsResourceMetadataAvailable(array) != true)
            throw new InvalidOperationException("External geometry changed during configuration acquisition.");
        return new(array, certification.Origin, certification.ConfigurationEpoch);
    }
    /// <summary>Checks a completed import against current manager identity and context knowledge without reading native configuration.</summary>
    internal static bool IsEngineGeometryConfigurationCurrent(RHIGeometryImport import)
    {
        var current = RHICommandContext.Current.CaptureGeometryContext(checked((int)import.Array.ResourceId));
        return import.IsCurrent(current.Origin, current.ConfigurationEpoch);
    }
    #endregion
}
