using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Retains engine pool culling and transform staging while owning every liquid draw's complete pipeline.</summary>
internal sealed class LiquidGraphicsSubmission : IDisposable
{
    [ThreadStatic] private static LiquidGraphicsSubmission? active;
    private readonly LiquidPoolPreparation geometry = new();
    private RHICommandContext? preparedContext;
    private LiquidTargetPreparation? targets;
    private DepthStencilDesc? preparedDepth;
    private ColorBlendDesc[] preparedBlends = [];
    private readonly Action unregister;
    private readonly ICoreClientAPI api;
    private GpuFramebuffer? borrowed;
    private RHICommandContext? borrowedContext;
    private GraphicsPipelineAsset? pipeline;
    private RHICommandContext? commands;
    private bool dirty = true;
    private GpuProgram? inputOwner;
    private LiquidPoolInputAdapter? inputAdapter;

    #region Public API
    /// <summary>Invalidates borrowed target metadata when the engine publishes rebuilt screen resources.</summary>
    internal LiquidGraphicsSubmission(ICoreClientAPI api)
    {
        this.api = api;
        unregister = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Invalidate);
        api.Event.LeaveWorld += LeaveWorld;
    }

    /// <summary>Publishes the engine target once per rebuild, retaining its exact attachment metadata.</summary>
    internal GpuFramebuffer Borrow(FrameBufferRef source)
    {
        var context = RHICommandContext.Current;
        if (dirty || !ReferenceEquals(borrowedContext, context) || borrowed is null || borrowed.FboId != source.FboId
            || borrowed.Width != source.Width || borrowed.Height != source.Height)
        {
            var candidate = DynamicGpuFramebuffer.Wrap(source.FboId, "Liquid.EngineTarget", source.Width, source.Height);
            try { candidate.PublishRenderPassMetadata(); }
            catch { candidate.Dispose(); throw; }
            borrowed?.Dispose(); borrowed = candidate; borrowedContext = context; dirty = false;
        }
        return borrowed;
    }

    /// <summary>Prepares storage before entry, then lets engine managers stage uniforms before intercepted complete draws.</summary>
    internal bool Run(GpuProgram shader, ReadOnlySpan<MeshDataPoolManager> managers, RHIRenderPassDescription pass,
        DepthStencilDesc depth, IReadOnlyList<ColorBlendDesc> blends, Action renderManagers)
    {
        if (!shader.EnsureReady()) return false;
        if (!ReferenceEquals(inputOwner, shader))
        {
            inputOwner = shader;
            inputAdapter = shader is ILiquidPoolInputs inputs ? new(inputs) : null;
        }
        var context = RHICommandContext.Current;
        if (preparedContext is not null && !ReferenceEquals(preparedContext, context))
        {
            Retire();
        }
        preparedContext = context;
        geometry.Prepare(managers);
        bool blendChanged = preparedBlends.Length != blends.Count;
        for (int i = 0; !blendChanged && i < blends.Count; i++) blendChanged = preparedBlends[i] != blends[i];
        if (pipeline is null || pipeline.Description.Shader != shader.Shader
            || preparedDepth != depth || blendChanged || targets is null || !targets.Matches(pass))
        {
            var signature = context.DescribeRenderTargets(pass);
            var targetCandidate = new LiquidTargetPreparation(pass);
            var blendCandidate = new ColorBlendDesc[blends.Count];
            for (int i = 0; i < blends.Count; i++) blendCandidate[i] = blends[i];
            var description = new RHIGraphicsPipelineDescription(shader.Shader, EngineLiquidPoolGeometry.Layout,
                signature, DynamicPipelineState.Viewport, depthStencil: depth, blending: blendCandidate);
            // Publish only after all candidate work succeeds. Equal target facts may still reuse the PSO.
            if (pipeline is null || pipeline.Description != description)
            {
                var candidate = new GraphicsPipelineAsset(shader, description);
                pipeline?.Dispose(); pipeline = candidate;
            }
            targets = targetCandidate; preparedDepth = depth; preparedBlends = blendCandidate;
        }
        if(!context.TryEnterBoundary(new(shader.PassName,true,graphics:[pipeline.Pipeline],operations:BoundaryOperations.RenderPasses),out var boundary)) return false;
        using(boundary) boundary!.Run(()=>
        {
            if (active is not null) throw new InvalidOperationException("Liquid pool submissions cannot nest.");
            context.BeginRenderPass(pass);
            context.SetPipeline(pipeline.Pipeline);
            context.SetDynamicState(new() { Viewport = context.PassViewport });
            // The narrow input bridge stages retained values before Draw publishes them.
            var engine = (Vintagestory.Client.RenderAPIBase)api.Render;
            bool previous = LiquidMeshSource.UseSsbo(engine);
            commands = context; active = this;
            try { LiquidMeshSource.UseSsbo(engine) = false; renderManagers(); context.EndRenderPass(); }
            finally { LiquidMeshSource.UseSsbo(engine) = previous; active = null; commands = null; }
        });
        return true;
    }

    /// <summary>Returns the input-only bridge solely during the existing active submission lifetime.</summary>
    internal static IShaderProgram? ActiveInputs => active == null ? null : active.inputAdapter
        ?? throw new InvalidOperationException("The selected liquid program has no pool input sink.");

    /// <summary>Replaces only a currently declared VGE liquid pool draw; unrelated engine pools retain their original path.</summary>
    internal static bool TryDraw(MeshDataPool pool)
    {
        if (active is null) return false;
        var geometry = active.geometry[pool];
        if (pool.indicesGroupsCount > 0)
        {
            var draws = geometry.CaptureDraws();
            active.commands!.BindResources(active.inputOwner!.CaptureBindings());
            active.commands.DrawIndexedBatch(geometry.Inputs, draws);
        }
        return true;
    }

    /// <summary>Retires realizations and metadata without disposing any engine storage.</summary>
    public void Dispose()
    {
        unregister(); api.Event.LeaveWorld -= LeaveWorld;
        LeaveWorld();
    }
    #endregion

    #region Private
    /// <summary>Withdraws borrowed identity after rebuild or world retirement, including reused native names.</summary>
    private void Invalidate() { dirty = true; targets = null; }

    /// <summary>Releases world/context preparation without deleting the engine allocations it borrows.</summary>
    private void Retire()
    {
        geometry.Dispose(); pipeline?.Dispose(); pipeline = null;
        targets = null; preparedContext = null;
    }

    /// <summary>Releases the old world's preparation and external surface association before possible native name reuse.</summary>
    private void LeaveWorld()
    {
        Retire(); borrowed?.Dispose(); borrowed = null; borrowedContext = null; dirty = true;
        inputOwner = null; inputAdapter = null;
    }
    #endregion
}
