using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Submits immediate graphics work within one declared, cache-backed engine boundary.</summary>
internal sealed class GraphicsCommandContext
{
    private readonly EngineBoundaryScope boundary;
    private readonly HashSet<GraphicsPipeline> pipelines;
    private RenderPass? pass;
    private GraphicsPipeline? pipeline;
    private GraphicsDynamicState dynamics = new();
    private ShaderActivation? activation;
    private bool submitting;
    private bool finished;

    /// <summary>Returns the active pass area for an explicit viewport assignment.</summary>
    internal DynamicDrawState PassViewport
    {
        get { RequireMutable(); return (pass ?? throw new InvalidOperationException("No active pass.")).Viewport; }
    }

    #region Public API
    #region Boundary lifetime
    /// <summary>Resolves the declared footprint once and restores engine ownership after sequential passes.</summary>
    internal static bool TryRun(string name, IReadOnlyList<GraphicsPipeline> pipelines,
        bool conditionalRenderingInactive, Action<GraphicsCommandContext> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return TryRunShared(name, pipelines, [], conditionalRenderingInactive, (context, _) => operation(context));
    }

    /// <summary>Shares complete restoration with declared compatibility shader work performed between completed passes.</summary>
    /// <remarks>The additional callbacks must use their existing tracked boundary adapters, never run inside an active pass.</remarks>
    internal static bool TryRunShared(string name, IReadOnlyList<GraphicsPipeline> pipelines,
        IReadOnlyList<GpuProgram> additionalPrograms, bool conditionalRenderingInactive,
        Action<GraphicsCommandContext, EngineBoundaryScope> operation)
    {
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(additionalPrograms);
        ArgumentNullException.ThrowIfNull(operation);
        var declared = pipelines.ToArray();
        foreach (var candidate in declared) { ArgumentNullException.ThrowIfNull(candidate); candidate.Validate(); }
        return CompleteGraphicsBoundary.TryRun(name, declared.Select(p => p.Description),
            declared.Select(p => p.Shader).Concat(additionalPrograms).Distinct(), conditionalRenderingInactive, boundary =>
            {
                var context = new GraphicsCommandContext(boundary, declared);
                // Boundary cleanup owns failures; do not mask an operation exception with local disposal.
                try { operation(context, boundary); }
                finally { context.finished = true; }
            });
    }

    /// <summary>Runs declared external work between submission boundaries, invalidating affected cache knowledge even on failure.</summary>
    internal static void ExecuteExternal(EPipelineState affected, Action operation)
    {
        // External work ends authority rather than suspending a snapshot while unknown code mutates it.
        StateCache.Current.ExecuteExternal(affected, operation);
    }
    #endregion

    #region Pass submission
    /// <summary>Begins a nonnested target pass and clears prior selection and dynamic values.</summary>
    internal void BeginPass(RenderPassDesc description)
    {
        RequireMutable();
        if (pass is not null) throw new InvalidOperationException("End the current pass before beginning another.");
        pass = RenderPass.Begin(description, boundary);
        pipeline = null;
        dynamics = new();
    }

    /// <summary>Selects only a prepared pipeline included in this boundary's restoration footprint.</summary>
    internal void SetPipeline(GraphicsPipeline value)
    {
        RequireMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (!pipelines.Contains(value)) throw new InvalidOperationException("Pipeline was not declared at boundary entry.");
        value.Validate();
        pipeline = value;
    }

    /// <summary>Copies immutable authored dynamic values without changing native state until a draw.</summary>
    internal void SetDynamicState(GraphicsDynamicState value)
    {
        RequireMutable();
        ArgumentNullException.ThrowIfNull(value);
        dynamics = value;
    }

    /// <summary>Validates the full draw, publishes current typed inputs, and only then emits native geometry.</summary>
    internal void Draw(GraphicsGeometry geometry, GraphicsDraw draw)
    {
        RequireMutable();
        ArgumentNullException.ThrowIfNull(geometry);
        var target = pass ?? throw new InvalidOperationException("Draw requires an active pass.");
        var selected = pipeline ?? throw new InvalidOperationException("Draw requires a selected pipeline.");
        submitting = true;
        try
        {
            target.ValidatePipeline(selected);
            geometry.Validate(selected.Description, draw);
            StateCache.Current.ApplyGraphicsState(selected, dynamics);
            if (activation is null || !ReferenceEquals(activation.Program, selected.Shader))
            {
                var previous = activation;
                activation = null;
                previous?.Dispose();
                activation = new ShaderActivation(selected.Shader);
                boundary.AddCleanup(EngineBoundaryCleanup.Shader, activation);
            }
            else selected.Shader.Use();
            // Generated submission can reject missing resources or UBO epochs. No draw precedes it.
            target.ValidatePipeline(selected);
            geometry.Validate(selected.Description, draw);
            geometry.Submit(draw);
        }
        finally { submitting = false; }
    }

    /// <summary>Releases shader ownership before restoring the pass's borrowed framebuffer bindings.</summary>
    internal void EndPass()
    {
        RequireMutable();
        var current = pass ?? throw new InvalidOperationException("No active pass.");
        var shader = activation;
        activation = null;
        // On failure the enclosing boundary still attempts framebuffer and draw-state cleanup.
        shader?.Dispose();
        current.Dispose();
        pass = null;
        pipeline = null;
        dynamics = new();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Retains the already resolved boundary and its exact pipeline set.</summary>
    private GraphicsCommandContext(EngineBoundaryScope boundary, IEnumerable<GraphicsPipeline> pipelines)
    {
        this.boundary = boundary;
        this.pipelines = new(pipelines);
    }

    /// <summary>Rejects escaped contexts and callbacks attempting to mutate an in-flight submission.</summary>
    private void RequireMutable()
    {
        ObjectDisposedException.ThrowIf(finished, this);
        if (submitting) throw new InvalidOperationException("Graphics submission cannot recurse or mutate its context.");
        StateCache.Current.RequireEngineBoundary(boundary);
    }

    /// <summary>Makes an existing shader ownership scope safe for both early and boundary cleanup.</summary>
    private sealed class ShaderActivation : IDisposable
    {
        private IDisposable? scope;
        internal GpuProgram Program { get; }
        /// <summary>Uses the engine-aware activation path, including generated input publication.</summary>
        internal ShaderActivation(GpuProgram program) { Program = program; scope = program.UseScope(); }
        /// <summary>Consumes the ownership scope once even when restoration throws.</summary>
        public void Dispose() { var value = scope; scope = null; value?.Dispose(); }
    }
    #endregion
}
