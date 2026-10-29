using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Admits authored external geometry without discovering or modifying the producer's array.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHIGeometryContext CaptureGeometryContext(int array)
    {
        RequireContext();
        return new(NativeOrigin, cache.GetVertexConfigurationRevision(array));
    }

    /// <summary>Validates supplied layout/ranges and records original dependencies for a private array realization.</summary>
    public void ValidateImportedGeometry(int array, RHIVertexLayout layout,
        IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices)
    {
        RequireContext();
        foreach (var stream in streams) RequireRange(stream.Range.Buffer, stream.Range.Offset, stream.Range.Length);
        RequireRange(indices.Range.Buffer, indices.Range.Offset, indices.Range.Length);
        foreach (var attribute in layout.Attributes)
        {
            if (attribute.Location >= NativeCapabilities.MaxVertexAttributes
                || attribute.Binding >= NativeCapabilities.MaxVertexBindings
                || attribute.Stride > NativeCapabilities.MaxVertexStride
                || attribute.Offset > NativeCapabilities.MaxVertexRelativeOffset)
                throw new ArgumentException("Imported geometry exceeds cached vertex-input limits.", nameof(layout));
            bool found = false;
            foreach (var stream in streams)
                if (stream.Binding == attribute.Binding) { found = true; break; }
            if (!found) throw new ArgumentException("The imported layout has an undeclared stream.", nameof(streams));
        }
        // The declaration describes the draw; it does not certify arbitrary external VAO state.
        // Existing backend realization builds a private array instead of repairing or scanning it.
        cache.EstablishVertexConfigurationDependencies(array, streams.Select(stream => checked((int)stream.Range.Buffer.RequireResource().ResourceId)),
            checked((int)indices.Range.Buffer.RequireResource().ResourceId));
    }
    #endregion
}