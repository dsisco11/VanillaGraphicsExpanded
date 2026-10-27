using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves noninstanced multi-draw batching using reusable backend scratch and semantic index intervals.</summary>
internal sealed partial class OpenGLBackend
{
    private int[] batchCounts = [], batchBaseVertices = [];
    private nint[] batchOffsets = [];

    #region Public API
    /// <inheritdoc />
    public unsafe void DrawIndexedBatch(RHIGraphicsPipelineRef reference, RHIGeometryInputs geometry,
        ReadOnlySpan<RHIDrawIndexed> draws, RHIDynamicState dynamics, RHIResourceBindings? resources)
    {
        RequireContext();
        var pipeline = reference.RequirePipeline();
        var shader = pipeline.RequireShader();
        ValidateGraphicsPacket(pipeline, geometry, dynamics);
        var indices = geometry.Indices ?? throw new InvalidOperationException("Indexed batches require an index stream.");
        int width = RHIGeometryInputs.IndexWidth(indices.Format);
        // Validate every supplied interval before changing any native state. Scratch arrays
        // are backend-owned and reused; immediate submission never copies into command storage.
        foreach (var draw in draws)
        {
            if (draw.InstanceCount != 1 || draw.BaseInstance != 0)
                throw new ArgumentException("Indexed batches require noninstanced intervals.", nameof(draws));
            ValidateIndexedDraw(geometry, draw);
            _ = checked(indices.Range.Offset + checked((nint)draw.FirstIndex * width));
        }
        if (draws.IsEmpty) return;
        pipeline.BeginSubmission();
        try
        {
            BeginBindingExecution(shader, resources);
            try
            {
                PrepareGraphicsPacket(pipeline, shader, geometry, dynamics, resources);
                if (batchCounts.Length < draws.Length)
                {
                    System.Array.Resize(ref batchCounts, draws.Length);
                    System.Array.Resize(ref batchBaseVertices, draws.Length);
                    System.Array.Resize(ref batchOffsets, draws.Length);
                }
                for (int index = 0; index < draws.Length; index++)
                {
                    batchCounts[index] = draws[index].IndexCount;
                    batchBaseVertices[index] = draws[index].BaseVertex;
                    batchOffsets[index] = checked(indices.Range.Offset + checked((nint)draws[index].FirstIndex * width));
                }
                MarkBindingExecution(shader, resources);
                fixed (int* counts = batchCounts, bases = batchBaseVertices)
                fixed (nint* offsets = batchOffsets)
                    GL.MultiDrawElementsBaseVertex(OpenGLPrimitiveValues.ToCore(pipeline.Description.Assembly.Topology),
                        counts, IndexType(indices.Format), (nint)offsets, draws.Length, bases);
                CompleteGraphicsPacket(geometry);
            }
            finally { ReleaseExecutionResources(); }
        }
        finally { pipeline.EndSubmission(); }
    }
    #endregion
}
