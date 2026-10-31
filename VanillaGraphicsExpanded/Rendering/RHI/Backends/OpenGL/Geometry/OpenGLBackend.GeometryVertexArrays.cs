using System;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes immutable geometry configurations using named core commands and the existing metadata authority.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<RHIGeometryInputs, GeometryVertexArray> geometryVertexArrays = new();

    #region Private
    /// <summary>Reuses a surviving configuration or publishes a complete candidate without exposing partial native setup.</summary>
    private GpuResource<GpuVertexArrayMetadata> ResolveGeometryVertexArray(RHIGeometryInputs geometry)
    {
        if (geometry.ImportedArray is { } imported)
        {
            imported.RequirePrepared(NativeOrigin);
        }
        if (geometryVertexArrays.TryGetValue(geometry, out var current))
        {
            if (current.IsCurrent(geometry.Revision)) return current.Array;
            geometryVertexArrays.Remove(geometry);
            current.Retire();
        }
        var array = CreateGeometryVertexArray();
        try
        {
            int elementBuffer = geometry.Indices is { } indices
                ? checked((int)indices.Range.Buffer.RequireResource().ResourceId) : 0;
            OpenGLVertexConfiguration.Execute(cache, array.VertexArrayId, elementBinding: true, () =>
            {
                // These arrays are backend-owned. No binding scope, feature variant,
                // descriptor readback or restoration of an unrelated selector is needed.
                foreach (var stream in geometry.Streams)
                {
                    OpenGLDriverCalls.VertexArrayVertexBuffer(array.VertexArrayId, stream.Binding,
                        checked((int)stream.Range.Buffer.RequireResource().ResourceId), stream.Range.Offset, stream.Stride);
                    RequireContext();
                    OpenGLDriverCalls.VertexArrayBindingDivisor(array.VertexArrayId, stream.Binding, stream.Divisor);
                    RequireContext();
                }
                foreach (var attribute in geometry.Layout.Attributes)
                {
                    OpenGLDriverCalls.VertexArrayAttribBinding(array.VertexArrayId, attribute.Location, attribute.Binding);
                    RequireContext();
                    if (attribute.Interpretation == VertexInterpretation.Double)
                        OpenGLDriverCalls.VertexArrayAttribLFormat(array.VertexArrayId, attribute.Location, attribute.Components,
                            VertexAttribDoubleType.Double, attribute.Offset);
                    else if (attribute.Interpretation == VertexInterpretation.Integer)
                        OpenGLDriverCalls.VertexArrayAttribIFormat(array.VertexArrayId, attribute.Location, attribute.Components,
                            OpenGLVertexValues.IntegerFormat(attribute.Storage), attribute.Offset);
                    else
                        OpenGLDriverCalls.VertexArrayAttribFormat(array.VertexArrayId, attribute.Location, attribute.Components,
                            OpenGLVertexValues.FloatingFormat(attribute.Storage),
                            attribute.Interpretation == VertexInterpretation.Normalized, attribute.Offset);
                    RequireContext();
                    OpenGLDriverCalls.EnableVertexArrayAttrib(array.VertexArrayId, attribute.Location);
                    RequireContext();
                }
                OpenGLDriverCalls.VertexArrayElementBuffer(array.VertexArrayId, elementBuffer);
                RequireContext();
            }, elementBuffer: elementBuffer);
            var realized = new GeometryVertexArray(array, geometry, cache);
            geometry.RetireWithBorrow(realized.Retire);
            geometryVertexArrays.Add(geometry, realized);
            return array;
        }
        catch
        {
            // Failed realization never replaces a usable packet or retires its borrowed buffers.
            try { array.Dispose(); } catch { }
            throw;
        }
    }

    /// <summary>Queues context-owned realizations without issuing native work during context-retirement callbacks.</summary>
    private void RetireGeometryVertexArrays()
    {
        foreach (var pair in geometryVertexArrays) pair.Value.Retire();
        geometryVertexArrays.Clear();
    }

    /// <summary>Owns one cached VAO while weak packet membership leaves resource references under their original owners.</summary>
    private sealed class GeometryVertexArray
    {
        private readonly RHIResourceDisposalQueue disposals = RHIResourceManagerSystem.CaptureDisposalQueue();
        private readonly ulong geometryRevision;
        private bool changed;

        private bool retired;

        #region Internal API
        /// <summary>Gets the solely backend-owned native array.</summary>
        internal GpuVao Array { get; }
        /// <summary>Captures completed configuration identity without native validation queries.</summary>
        internal GeometryVertexArray(GpuVao array, RHIGeometryInputs geometry, OpenGlStateCache cache)
        {
            Array = array;
            geometryRevision = geometry.Revision;
            array.ObservePreparation(this, Withdraw);
            cache.ObserveVertexConfiguration(array.VertexArrayId, this, Withdraw);
            foreach (var stream in geometry.Streams) stream.Range.Buffer.RequireResource().ObservePreparation(this, Retire);
            if (geometry.Indices is { } indices) indices.Range.Buffer.RequireResource().ObservePreparation(this, Retire);
        }
        /// <summary>Tests only delivered configuration changes and the semantic preparation revision.</summary>
        internal bool IsCurrent(ulong revision) => !retired && !changed && geometryRevision == revision;
        /// <summary>Withdraws only this realized array without scanning other cached geometry.</summary>
        private void Withdraw() { changed = true; Retire(); }
        /// <summary>Transfers cleanup to the existing originating-lifetime queue once, including from finalization.</summary>
        internal void Retire()
        {
            if (retired) return;
            retired = true;
            disposals.Enqueue(Array);
            GC.SuppressFinalize(this);
        }
        #endregion

        /// <summary>Queues abandoned weak-cache entries without calling native APIs on the finalizer thread.</summary>
        ~GeometryVertexArray() => Retire();
    }
    #endregion
}
