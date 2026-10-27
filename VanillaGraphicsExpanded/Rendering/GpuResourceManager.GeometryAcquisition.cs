using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cold external geometry configuration acquisition without adding a canonical VAO layout mirror.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Certifies the original external array against declared streams and retains the completed configuration revision.</summary>
    internal static RHIGeometryImport AcquireEngineGeometryConfiguration(GpuResource<GpuVertexArrayMetadata> array,
        RHIVertexLayout layout, IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices)
    {
        RequireWrapperMetadataAccess(array);
        ObjectDisposedException.ThrowIf(!array.IsValid, array);
        var execution = RHICommandContext.Current;
        var original = array.ResourceManager?.GetResourceRegistration(array);
        if (original is not null && array.ResourceManager?.IsResourceMetadataAvailable(array) != true)
            throw new InvalidOperationException("External vertex-array metadata is unavailable.");
        execution.CertifyGeometryConfiguration(checked((int)array.ResourceId), layout, streams, indices);
        var certification = execution.CaptureGeometryContext();
        if (!array.IsValid || !ReferenceEquals(original, array.ResourceManager?.GetResourceRegistration(array))
            || original is not null && array.ResourceManager?.IsResourceMetadataAvailable(array) != true)
            throw new InvalidOperationException("External geometry changed during configuration acquisition.");
        return new(array, certification.Origin, certification.ConfigurationEpoch);
    }
    /// <summary>Checks a completed import against current manager identity and context knowledge without reading native configuration.</summary>
    internal static bool IsEngineGeometryConfigurationCurrent(RHIGeometryImport import)
    {
        var current = RHICommandContext.Current.CaptureGeometryContext();
        return import.IsCurrent(current.Origin, current.ConfigurationEpoch);
    }
    #endregion
}
