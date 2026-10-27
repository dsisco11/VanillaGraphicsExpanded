using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.OpenGL;

/// <summary>Admits an engine-owned realized VAO and its original buffer allocations without acquiring native ownership.</summary>
internal sealed class EngineGeometryInputs : IDisposable
{
    private readonly List<GpuResource> recipients = new();
    private readonly RHIGeometryBorrow borrow = new();
    private bool disposed;

    #region Internal API
    /// <summary>Gets the certified packet; adapters expose this data instead of issuing an engine draw.</summary>
    internal RHIGeometryInputs Inputs { get; }

    /// <summary>Captures native identity from the engine owner and certifies only externally supplied configuration.</summary>
    internal EngineGeometryInputs(VAO mesh, RHIVertexLayout layout, IReadOnlyDictionary<int, int> streams,
        int vertices, int indices, int instances = 1)
    {
        if (mesh.Disposed || !mesh.Initialized || mesh.VaoId <= 0 || mesh.vboIdIndex <= 0
            || vertices <= 0 || indices <= 0 || instances <= 0)
            throw new ArgumentException("Engine geometry has no complete realized storage.", nameof(mesh));
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        try
        {
            var array = new ArrayRecipient(mesh);
            recipients.Add(array);
            var vertexStreams = new List<RHIVertexStream>();
            foreach (var group in layout.Attributes.GroupBy(attribute => attribute.Binding))
            {
                var first = group.First();
                if (!streams.TryGetValue(first.Binding, out int name) || name <= 0)
                    throw new ArgumentException("Engine geometry is missing a declared stream.", nameof(streams));
                var buffer = new BufferRecipient(name);
                recipients.Add(buffer);
                long count = first.Divisor == 0 ? vertices : (instances - 1L) / first.Divisor + 1;
                nint bytes = checked((nint)(count * first.Stride));
                vertexStreams.Add(new(first.Binding, new(new GpuBufferRef(buffer), 0, bytes), first.Stride, first.Divisor));
            }
            var elements = new BufferRecipient(mesh.vboIdIndex);
            recipients.Add(elements);
            var indexStream = new RHIIndexStream(new(new GpuBufferRef(elements), 0, checked((nint)indices * sizeof(uint))));
            // The manager owns acquisition and its native reader. The resulting packet
            // retains the original revision and must be re-admitted after external mutation.
            var certificate = GpuResourceManager.AcquireEngineGeometryConfiguration(array, layout, vertexStreams, indexStream, indexSelectedAtDraw: true);
            Inputs = new(layout, vertexStreams, indexStream, certificate, borrow);
        }
        catch { Dispose(); throw; }
    }
    #endregion

    #region Public API
    /// <summary>Expires consumers and unregisters recipients while leaving all engine allocations owned by the engine.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        borrow.Dispose();
        foreach (var recipient in recipients) recipient.Dispose();
        recipients.Clear();
    }
    #endregion

    #region Private
    /// <summary>Receives canonical buffer facts for one engine-owned allocation.</summary>
    private sealed class BufferRecipient : GpuResource<GpuBufferMetadata>
    {
        private static readonly GpuBufferMetadataReader reader = new();
        private nint id;

        #region Public API
        /// <inheritdoc />
        public override nint ResourceId { get => id; protected set => id = value; }
        /// <summary>Leaves native labels under the external owner's authority.</summary>
        public override void SetDebugName(string? name) { }
        #endregion

        #region Internal API
        /// <summary>Reuses available canonical facts or acquires missing external storage once.</summary>
        internal BufferRecipient(int name)
        {
            SetCreationIdentity(name, CaptureCreationContext(GpuResourceKind.Buffer));
            try { GpuResourceManager.AcquireWrapperMetadata(this, reader); }
            catch { Dispose(); throw; }
        }
        #endregion

        #region Protected API
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        #endregion
    }

    /// <summary>Receives empty-schema VAO metadata from a source-proven realized engine allocation.</summary>
    private sealed class ArrayRecipient : GpuResource<GpuVertexArrayMetadata>
    {
        private nint id;

        #region Public API
        /// <inheritdoc />
        public override nint ResourceId { get => id; protected set => id = value; }
        /// <summary>Leaves native labels under the external owner's authority.</summary>
        public override void SetDebugName(string? name) { }
        #endregion

        #region Internal API
        /// <summary>Associates only the actual initialized array supplied by the engine upload owner.</summary>
        internal ArrayRecipient(VAO mesh)
        {
            SetCreationIdentity(mesh.VaoId, CaptureCreationContext(GpuResourceKind.VertexArray));
            try { GpuResourceManager.AcquireWrapperMetadata(this, new ArrayReader(mesh)); }
            catch { Dispose(); throw; }
        }
        #endregion

        #region Protected API
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.VertexArray;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        #endregion
    }

    /// <summary>Uses the engine's realized source identity for the empty VAO schema, without an object-validity query.</summary>
    private sealed class ArrayReader : IGpuResourceMetadataReader<GpuVertexArrayMetadata>
    {
        private readonly WeakReference<VAO> source;

        #region Internal API
        /// <summary>Retains source proof without preventing the engine owner's normal collection and finalization.</summary>
        internal ArrayReader(VAO mesh) => source = new(mesh);
        #endregion

        #region Public API
        /// <inheritdoc />
        public bool IsResource(nint id) => id > 0 && source.TryGetTarget(out var mesh)
            && id == mesh.VaoId && mesh.Initialized && !mesh.Disposed;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuVertexArrayMetadata metadata)
        {
            metadata = default;
            return IsResource(id);
        }
        #endregion
    }
    #endregion
}
