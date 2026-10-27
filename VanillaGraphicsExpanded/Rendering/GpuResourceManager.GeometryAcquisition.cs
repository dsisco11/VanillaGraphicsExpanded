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
        var backend = RHICommandContext.Current.OpenGL;
        var original = array.ResourceManager?.GetResourceRegistration(array);
        if (original is not null && array.ResourceManager?.IsResourceMetadataAvailable(array) != true)
            throw new InvalidOperationException("External vertex-array metadata is unavailable.");
        backend.CertifyGeometryConfiguration(checked((int)array.ResourceId), layout, streams, indices);
        backend.RequireContext();
        if (!array.IsValid || !ReferenceEquals(original, array.ResourceManager?.GetResourceRegistration(array))
            || original is not null && array.ResourceManager?.IsResourceMetadataAvailable(array) != true)
            throw new InvalidOperationException("External geometry changed during configuration acquisition.");
        return new(array, backend.NativeOrigin, StateCache.Current.VertexConfigurationEpoch);
    }
    #endregion
}
