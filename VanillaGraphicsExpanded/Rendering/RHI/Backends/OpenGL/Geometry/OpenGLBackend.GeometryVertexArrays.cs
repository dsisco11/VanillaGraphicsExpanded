using System;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes immutable geometry configurations using named core commands and the existing metadata authority.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<RHIGeometryInputs, GeometryVertexArray> geometryVertexArrays = new();

    #region Internal API
    /// <summary>Retires only cached arrays that still retain a deleted buffer or the deleted original array.</summary>
    internal void InvalidateGeometryRealizations(GpuResourceKind kind, int id)
    {
        if (kind is not (GpuResourceKind.Buffer or GpuResourceKind.VertexArray)) return;
        var affected = new System.Collections.Generic.List<RHIGeometryInputs>();
        foreach (var pair in geometryVertexArrays)
            if (pair.Value.DependsOn(kind, id))
            {
                pair.Value.Retire();
                affected.Add(pair.Key);
            }
        foreach (var geometry in affected) geometryVertexArrays.Remove(geometry);
    }
    #endregion

    #region Private
    /// <summary>Reuses a surviving configuration or publishes a complete candidate without exposing partial native setup.</summary>
    private GpuResource<GpuVertexArrayMetadata> ResolveGeometryVertexArray(RHIGeometryInputs geometry)
    {
        if (geometry.ImportedArray is { } imported)
        {
            imported.RequireCurrent(NativeOrigin, cache.GetVertexConfigurationRevision(checked((int)imported.Array.ResourceId)));
            if (imported.CanUseNativeArray) return imported.Array;
        }
        if (geometryVertexArrays.TryGetValue(geometry, out var current))
        {
            if (current.Matches(geometry, cache)) return current.Array;
            geometryVertexArrays.Remove(geometry);
            current.Retire();
        }
        var array = GpuVao.Create("RHI.Geometry");
        try
        {
            var observation = StateCache.BeginNativeVertexMetadata(array.VertexArrayId, named: true, elementBinding: true);
            bool returned = false;
            int elementBuffer = geometry.Indices is { } indices
                ? checked((int)indices.Range.Buffer.RequireResource().ResourceId) : 0;
            try
            {
                // These arrays are backend-owned. No binding scope, feature variant,
                // descriptor readback or restoration of an unrelated selector is needed.
                foreach (var stream in geometry.Streams)
                {
                    GL.VertexArrayVertexBuffer(array.VertexArrayId, stream.Binding,
                        checked((int)stream.Range.Buffer.RequireResource().ResourceId), stream.Range.Offset, stream.Stride);
                    RequireContext();
                    GL.VertexArrayBindingDivisor(array.VertexArrayId, stream.Binding, stream.Divisor);
                    RequireContext();
                }
                foreach (var attribute in geometry.Layout.Attributes)
                {
                    GL.VertexArrayAttribBinding(array.VertexArrayId, attribute.Location, attribute.Binding);
                    RequireContext();
                    if (attribute.Interpretation == VertexInterpretation.Double)
                        GL.VertexArrayAttribLFormat(array.VertexArrayId, attribute.Location, attribute.Components,
                            VertexAttribDoubleType.Double, attribute.Offset);
                    else if (attribute.Interpretation == VertexInterpretation.Integer)
                        GL.VertexArrayAttribIFormat(array.VertexArrayId, attribute.Location, attribute.Components,
                            OpenGLVertexValues.IntegerFormat(attribute.Storage), attribute.Offset);
                    else
                        GL.VertexArrayAttribFormat(array.VertexArrayId, attribute.Location, attribute.Components,
                            OpenGLVertexValues.FloatingFormat(attribute.Storage),
                            attribute.Interpretation == VertexInterpretation.Normalized, attribute.Offset);
                    RequireContext();
                    GL.EnableVertexArrayAttrib(array.VertexArrayId, attribute.Location);
                    RequireContext();
                }
                GL.VertexArrayElementBuffer(array.VertexArrayId, elementBuffer);
                RequireContext();
                returned = true;
            }
            finally { observation.Complete(returned); }
            observation.EstablishElementBinding(elementBuffer);
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
        private readonly GpuResourceDisposalQueue disposals = GpuResourceManagerSystem.CaptureDisposalQueue();
        private readonly GpuResourceRegistration? registration;
        private readonly ulong revision, localEpoch;
        private readonly int[] streamNames;
        private readonly int elementName;
        private bool retired;

        #region Internal API
        /// <summary>Gets the solely backend-owned native array.</summary>
        internal GpuVao Array { get; }
        /// <summary>Captures completed configuration identity without native validation queries.</summary>
        internal GeometryVertexArray(GpuVao array, RHIGeometryInputs geometry, StateCache cache)
        {
            Array = array;
            registration = array.ResourceManager?.GetResourceRegistration(array);
            revision = registration?.MetadataRevision ?? 0;
            localEpoch = cache.GetVertexConfigurationRevision(Array.VertexArrayId);
            streamNames = new int[geometry.Streams.Length];
            for (int index = 0; index < streamNames.Length; index++)
                streamNames[index] = checked((int)geometry.Streams[index].Range.Buffer.RequireResource().ResourceId);
            elementName = geometry.Indices is { } indices
                ? checked((int)indices.Range.Buffer.RequireResource().ResourceId) : 0;
        }
        /// <summary>Checks original array identity and observed configuration changes without inspecting driver state.</summary>
        internal bool Matches(RHIGeometryInputs geometry, StateCache cache)
        {
            if (retired || !Array.IsValid || !ReferenceEquals(registration, Array.ResourceManager?.GetResourceRegistration(Array))) return false;
            if (registration is not null && Array.ResourceManager?.IsResourceMetadataAvailable(Array) != true) return false;
            if (registration is not null ? registration.MetadataRevision != revision : localEpoch != cache.GetVertexConfigurationRevision(Array.VertexArrayId)) return false;
            for (int index = 0; index < streamNames.Length; index++)
                if (streamNames[index] != geometry.Streams[index].Range.Buffer.RequireResource().ResourceId) return false;
            return elementName == (geometry.Indices is { } indices ? indices.Range.Buffer.RequireResource().ResourceId : 0);
        }
        /// <summary>Checks the names captured by this realization before numeric reuse can occur.</summary>
        internal bool DependsOn(GpuResourceKind kind, int id)
        {
            if (kind == GpuResourceKind.VertexArray) return Array.VertexArrayId == id;
            if (elementName == id) return true;
            foreach (int name in streamNames) if (name == id) return true;
            return false;
        }
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
