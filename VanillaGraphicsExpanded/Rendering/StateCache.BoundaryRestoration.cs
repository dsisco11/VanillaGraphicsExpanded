using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Restores resolved boundary values through the existing transition owners.</summary>
internal sealed partial class StateCache
{
    private bool restoringBoundary;
    #region Public API
    /// <summary>Checks context lifetime before any cleanup owner can touch native state.</summary>
    internal void RequireBoundaryContext(PipelineStateSnapshot snapshot)
    {
        SynchronizeContext();
        if (context != snapshot.Context || context.Generation == 0)
            throw new InvalidOperationException("Cannot restore an engine boundary into another context generation.");
    }

    /// <summary>Attempts an independent cleanup owner without hiding failures from later owners.</summary>
    internal void AttemptBoundaryCleanup(PipelineStateSnapshot snapshot, Action cleanup, List<Exception> failures,
        bool shaderOwnership = false)
    {
        try
        {
            RequireBoundaryContext(snapshot);
            restoringBoundary = true;
            CheckBoundaryNativeError();
            cleanup();
            CheckBoundaryNativeError();
        }
        catch (Exception error)
        {
            if (shaderOwnership) Invalidate(EPipelineState.Program);
            failures.Add(error);
        }
        finally { restoringBoundary = false; }
    }

    /// <summary>Restores only explicitly covered values, preserving mixed output state and unrelated knowledge.</summary>
    internal void RestoreBoundaryState(PipelineStateSnapshot snapshot, List<Exception> failures)
    {
        try { RequireBoundaryContext(snapshot); }
        catch (Exception error) { failures.Add(error); return; }
        var coverage = snapshot.Coverage;
        if (coverage.Depth.HasFlag(DepthStateKnowledge.TestEnabled))
            RestoreBoundaryField(snapshot, () => SetCapability(EnableCap.DepthTest, snapshot.Depth.TestEnabled), () => depthKnown &= ~DepthStateKnowledge.TestEnabled, failures);
        if (coverage.Depth.HasFlag(DepthStateKnowledge.Comparison))
            RestoreBoundaryField(snapshot, () => SetDepthFunc(snapshot.Depth.Comparison), () => depthKnown &= ~DepthStateKnowledge.Comparison, failures);
        if (coverage.Depth.HasFlag(DepthStateKnowledge.WriteEnabled))
            RestoreBoundaryField(snapshot, () => SetDepthWriteMask(snapshot.Depth.WriteEnabled), () => depthKnown &= ~DepthStateKnowledge.WriteEnabled, failures);
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.CullEnabled))
            RestoreBoundaryField(snapshot, () => SetCapability(EnableCap.CullFace, snapshot.Rasterizer.CullEnabled), () => rasterizerKnown &= ~RasterizerStateKnowledge.CullEnabled, failures);
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.ScissorEnabled))
            RestoreBoundaryField(snapshot, () => SetCapability(EnableCap.ScissorTest, snapshot.Rasterizer.ScissorEnabled), () => rasterizerKnown &= ~RasterizerStateKnowledge.ScissorEnabled, failures);
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth))
            RestoreBoundaryField(snapshot, () => SetLineWidth(snapshot.Rasterizer.LineWidth), () => rasterizerKnown &= ~RasterizerStateKnowledge.LineWidth, failures);
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize))
            RestoreBoundaryField(snapshot, () => SetPointSize(snapshot.Rasterizer.PointSize), () => rasterizerKnown &= ~RasterizerStateKnowledge.PointSize, failures);
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.ProvokingVertex))
            RestoreBoundaryField(snapshot, () => SetProvokingVertex(snapshot.Rasterizer.ProvokingVertex), () => rasterizerKnown &= ~RasterizerStateKnowledge.ProvokingVertex, failures);
        if (coverage.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices))
            RestoreBoundaryField(snapshot, () => SetPatchVertices(snapshot.Assembly.PatchVertices), () => assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PatchVertices, failures);
        if (coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.Viewport))
            RestoreBoundaryField(snapshot, () => ApplyDynamic(snapshot.Dynamic), () => dynamicKnown &= ~DynamicDrawStateKnowledge.Viewport, failures);
        if (coverage.ClearColor)
            RestoreBoundaryField(snapshot, () => SetClearColor(snapshot.ClearColor.X, snapshot.ClearColor.Y,
                snapshot.ClearColor.Z, snapshot.ClearColor.W), () => clearColorKnown = false, failures);
        // Indexed restoration has no trailing global operation that could overwrite mixed values.
        for (int output = 0; output < snapshot.OutputCount; output++)
        {
            int index = output;
            var saved = snapshot.BlendAt(index);
            var covered = coverage.BlendAt(index);
            if (covered.HasFlag(BlendStateKnowledge.Enabled))
                RestoreBoundaryField(snapshot, () => SetBlendEnabledIndexed(index, saved.Enabled), () => blendKnown[index] &= ~BlendStateKnowledge.Enabled, failures);
            if (covered.HasFlag(BlendStateKnowledge.Factors))
                RestoreBoundaryField(snapshot, () => SetBlendFuncIndexed(index, saved.Factors), () => blendKnown[index] &= ~BlendStateKnowledge.Factors, failures);
            if (covered.HasFlag(BlendStateKnowledge.WriteMask))
                RestoreBoundaryField(snapshot, () => SetColorMaskIndexed(index, saved.WriteMask), () => blendKnown[index] &= ~BlendStateKnowledge.WriteMask, failures);
        }
    }

    /// <summary>Runs an explicitly external operation outside managed authority and withdraws only affected knowledge.</summary>
    internal void ExecuteExternal(EPipelineState affected, Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!EPipelineState.All.HasFlag(affected)) throw new ArgumentOutOfRangeException(nameof(affected));
        if (activeBoundary is not null || resolvingBoundary)
            throw new InvalidOperationException("End the engine boundary before an unknown external operation.");
        try { operation(); }
        finally { Invalidate(affected); }
    }
    #endregion

    #region Private
    /// <summary>Leaves exactly the failed field unknown, including errors reported by native GL rather than exceptions.</summary>
    private void RestoreBoundaryField(PipelineStateSnapshot snapshot, Action restore, Action forget, List<Exception> failures)
    {
        try
        {
            RequireBoundaryContext(snapshot);
            restoringBoundary = true;
            CheckBoundaryNativeError();
            restore();
            CheckBoundaryNativeError();
        }
        catch (Exception error)
        {
            failures.Add(error);
            // A context switch may already have discarded the payload being invalidated.
            try { forget(); }
            catch (Exception invalidationFailure) { failures.Add(invalidationFailure); }
        }
        finally { restoringBoundary = false; }
    }

    /// <summary>Turns driver errors into a visible failed handoff rather than false cached success.</summary>
    private void CheckBoundaryNativeError()
    {
        var error = ReadBoundaryError();
        if (error != ErrorCode.NoError) throw new InvalidOperationException($"Native boundary cleanup failed: {error}.");
    }
    #endregion
}
