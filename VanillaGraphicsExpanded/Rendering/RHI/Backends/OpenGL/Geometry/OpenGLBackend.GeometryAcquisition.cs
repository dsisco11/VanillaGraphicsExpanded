using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reads externally configured vertex interpretation only at the manager's explicit admission boundary.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHIGeometryContext CaptureGeometryContext(int array)
    {
        RequireContext();
        return new(NativeOrigin, cache.GetVertexConfigurationRevision(array));
    }

    /// <summary>Checks named external configuration and published capacities without changing any native selector.</summary>
    public bool CertifyGeometryConfiguration(int array, RHIVertexLayout layout,
        IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices, bool indexSelectedAtDraw)
    {
        RequireContext();
        // Attribute-to-binding and binding-slot facts use the indexed queries defined for the selected VAO.
        using var selection=cache.BindVertexArrayScope(array);
        foreach (var stream in streams) RequireRange(stream.Range.Buffer, stream.Range.Offset, stream.Range.Length);
        RequireRange(indices.Range.Buffer, indices.Range.Offset, indices.Range.Length);
        foreach (var attribute in layout.Attributes)
        {
            if (attribute.Location >= NativeCapabilities.MaxVertexAttributes
                || attribute.Binding >= NativeCapabilities.MaxVertexBindings
                || attribute.Stride > NativeCapabilities.MaxVertexStride
                || attribute.Offset > NativeCapabilities.MaxVertexRelativeOffset)
                throw new ArgumentException("Imported geometry exceeds cached vertex-input limits.", nameof(layout));
            RHIVertexStream stream = default;
            bool found = false;
            foreach (var candidate in streams)
                if (candidate.Binding == attribute.Binding) { stream = candidate; found = true; break; }
            if (!found) throw new ArgumentException("The imported layout has an undeclared stream.", nameof(streams));
            int binding = ReadGeometryInteger(array, attribute.Location, All.VertexAttribBinding);
            int stride = ReadGeometryInteger(array, binding, All.VertexBindingStride);
            if (stride == 0) stride = checked(attribute.Components * VertexScalarWidth(attribute.Storage));
            long offset = -1;
            GL.GetVertexArrayIndexed64(array, binding, VertexArrayIndexed64Parameter.VertexBindingOffset, out offset);
            RequireContext();
            if (ReadGeometryInteger(array, attribute.Location, All.VertexAttribArrayEnabled) != 1
                || ReadGeometryInteger(array, attribute.Location, All.VertexAttribArraySize) != attribute.Components
                || ReadGeometryInteger(array, attribute.Location, All.VertexAttribArrayType) != (int)OpenGLVertexValues.FloatingFormat(attribute.Storage)
                || ReadGeometryInteger(array, attribute.Location, All.VertexAttribArrayNormalized) != (attribute.Interpretation == VertexInterpretation.Normalized ? 1 : 0)
                || ReadGeometryInteger(array, attribute.Location, All.VertexAttribArrayInteger) != (attribute.Interpretation == VertexInterpretation.Integer ? 1 : 0)
                || ReadGeometryInteger(array, attribute.Location, All.VertexAttribArrayLong) != (attribute.Interpretation == VertexInterpretation.Double ? 1 : 0)
                || ReadGeometryInteger(array, binding, All.VertexBindingBuffer) != stream.Range.Buffer.RequireResource().ResourceId
                || ReadGeometryInteger(array, binding, All.VertexBindingDivisor) != stream.Divisor
                || stride != stream.Stride || offset < 0
                || offset + ReadGeometryInteger(array, attribute.Location, All.VertexAttribRelativeOffset) != (long)stream.Range.Offset + attribute.Offset)
                throw new InvalidOperationException("The external vertex array disagrees with its declared stream interpretation.");
        }
        GL.GetVertexArray(array, (VertexArrayParameter)All.ElementArrayBufferBinding, out int elements);
        RequireContext();
        // Ordinary engine meshes may select their index buffer only at draw time. Do not mutate that array:
        // certify the detached state, then realize a private VAO from the validated streams for RHI draws.
        if (elements != indices.Range.Buffer.RequireResource().ResourceId && !(indexSelectedAtDraw && elements == 0))
            throw new InvalidOperationException("The external array uses a different index allocation.");
        cache.EstablishVertexArrayElementKnowledge(array, elements);
        cache.EstablishVertexConfigurationDependencies(array, streams.Select(stream => checked((int)stream.Range.Buffer.RequireResource().ResourceId)),
            checked((int)indices.Range.Buffer.RequireResource().ResourceId));
        return elements != 0;
    }
    #endregion

    #region Private
    /// <summary>Reads one represented external field and rejects failed-query sentinels without draining native error state.</summary>
    private int ReadGeometryInteger(int array, int selector, All parameter)
    {
        int value = -1;
        if(parameter is All.VertexBindingBuffer or All.VertexBindingStride or All.VertexBindingDivisor)
            GL.GetInteger((GetIndexedPName)parameter,selector,out value);
        else GL.GetVertexAttrib(selector,(VertexAttribParameter)parameter,out value);
        RequireContext();
        if (value < 0) throw new InvalidOperationException($"External vertex configuration could not be acquired: array {array}, selector {selector}, parameter {parameter}.");
        return value;
    }
    #endregion
}
