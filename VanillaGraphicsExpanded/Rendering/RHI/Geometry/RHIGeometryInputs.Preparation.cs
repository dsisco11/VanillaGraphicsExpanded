using System;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Prepares buffer relationships and reusable fetch limits on relevant configuration changes.</summary>
public sealed partial class RHIGeometryInputs
{
    private bool dirty = true;
    private Exception? preparationFailure;
    private IRHIContextLifetime? origin;
    private readonly ConditionalWeakTable<RHIGraphicsPipeline, object> compatiblePipelines = new();
    internal ulong Revision { get; private set; }
    internal FetchLimit[] FetchLimits { get; private set; } = [];
#if DEBUG
    internal long Preparations { get; private set; }
#endif

    /// <summary>Stores the maximum legal attribute index for one stream's packing.</summary>
    internal readonly record struct FetchLimit(long Last, int Divisor);

    #region Internal API
    /// <summary>Establishes resource bounds once and retains failed preparation until a dependency changes.</summary>
    internal void Prepare(RHICapabilities capabilities, IRHIContextLifetime context, RHIGraphicsPipeline pipeline)
    {
        origin ??= context;
        origin.RequireContext();
        RequireLive();
        if (dirty)
        {
            dirty = false;
            preparationFailure = null;
#if DEBUG
            Preparations++;
#endif
            try
            {
                var limits = new FetchLimit[Streams.Length];
                foreach (var attribute in Layout.Attributes)
                    if (attribute.Location >= capabilities.MaxVertexAttributes || attribute.Binding >= capabilities.MaxVertexBindings
                        || attribute.Stride > capabilities.MaxVertexStride || attribute.Offset > capabilities.MaxVertexRelativeOffset)
                        throw new ArgumentException("Vertex layout exceeds cached device limits.");
                for (int index = 0; index < Streams.Length; index++)
                {
                    var stream = Streams[index];
                    PrepareRange(stream.Range);
                    long last = long.MaxValue;
                    foreach (var attribute in Layout.Attributes)
                    {
                        if (attribute.Binding != stream.Binding) continue;
                        long available = (long)stream.Range.Length - attribute.Offset - ScalarWidth(attribute.Storage) * attribute.Components;
                        last = Math.Min(last, available < 0 ? -1 : stream.Stride == 0 ? long.MaxValue : available / stream.Stride);
                    }
                    limits[index] = new(last, stream.Divisor);
                }
                if (Indices is { } indices) PrepareRange(indices.Range);
                FetchLimits = limits;
            }
            catch (Exception error) { preparationFailure = error; FetchLimits = []; }
        }
        if (preparationFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(preparationFailure).Throw();
        if (!compatiblePipelines.TryGetValue(pipeline, out _))
        {
            if (Layout != pipeline.Description.VertexLayout) throw new InvalidOperationException("Geometry layout does not match the selected graphics pipeline.");
            compatiblePipelines.Add(pipeline, new object());
        }
    }
    #endregion

    #region Private
    /// <summary>Subscribes the actual selected owners without retaining native lifetime authority.</summary>
    private void ObserveGeometry()
    {
        foreach (var stream in Streams) stream.Range.Buffer.RequireResource().ObservePreparation(this, InvalidateGeometry);
        if (Indices is { } indices) indices.Range.Buffer.RequireResource().ObservePreparation(this, InvalidateGeometry);
    }
    /// <summary>Withdraws derived bounds and native realization after a coherent structural change.</summary>
    private void InvalidateGeometry() { dirty = true; Revision++; }
    /// <summary>Checks the original published allocation without reading index or indirect contents.</summary>
    private static void PrepareRange(RHIBufferRange range)
    {
        var facts = RHIResourceManager.RequireBufferReferenceMetadata(range.Buffer.RequireResource());
        if (range.Offset > facts.Capacity || range.Length > facts.Capacity - range.Offset)
            throw new ArgumentOutOfRangeException(nameof(range), "Geometry exceeds its selected buffer allocation.");
    }
    /// <summary>Returns semantic scalar byte width for fetch-limit derivation.</summary>
    private static int ScalarWidth(VertexComponentFormat format) => format switch
    {
        VertexComponentFormat.SignedByte or VertexComponentFormat.UnsignedByte => 1,
        VertexComponentFormat.SignedShort or VertexComponentFormat.UnsignedShort or VertexComponentFormat.Half => 2,
        VertexComponentFormat.SignedInt or VertexComponentFormat.UnsignedInt or VertexComponentFormat.Float => 4,
        VertexComponentFormat.Double => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    #endregion
}
