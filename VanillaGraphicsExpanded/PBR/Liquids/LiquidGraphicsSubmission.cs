using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
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
    private static readonly AccessTools.FieldRef<MeshDataPoolManager, List<MeshDataPool>> ReadPools =
        AccessTools.FieldRefAccess<MeshDataPoolManager, List<MeshDataPool>>("pools");
    [ThreadStatic] private static LiquidGraphicsSubmission? active;
    private readonly Dictionary<MeshDataPool, EngineLiquidPoolGeometry> geometry = new();
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Action unregister;
    private readonly ICoreClientAPI api;
    private GpuFramebuffer? borrowed;
    private GraphicsPipeline? pipeline;
    private GraphicsCommandContext? commands;
    private bool dirty = true;
    private GpuProgram? inputOwner;
    private LiquidPoolInputAdapter? inputAdapter;

    #region Public API
    /// <summary>Invalidates borrowed target metadata when the engine publishes rebuilt screen resources.</summary>
    internal LiquidGraphicsSubmission(ICoreClientAPI api)
    {
        this.api = api;
        unregister = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Invalidate);
        api.Event.LeaveWorld += Invalidate;
    }

    /// <summary>Publishes the engine target once per rebuild, retaining its exact attachment metadata.</summary>
    internal GpuFramebuffer Borrow(FrameBufferRef source)
    {
        if (dirty || borrowed is null || borrowed.FboId != source.FboId
            || borrowed.Width != source.Width || borrowed.Height != source.Height)
        {
            var candidate = GpuFramebuffer.Wrap(source.FboId, "Liquid.EngineTarget", source.Width, source.Height);
            try { candidate.PublishRenderPassMetadata(); }
            catch { candidate.Dispose(); throw; }
            borrowed?.Dispose(); borrowed = candidate; dirty = false;
        }
        return borrowed;
    }

    /// <summary>Prepares storage before entry, then lets engine managers stage uniforms before intercepted complete draws.</summary>
    internal bool Run(GpuProgram shader, MeshDataPoolManager[] managers, RenderPassDesc pass,
        DepthStencilDesc depth, IReadOnlyList<ColorBlendDesc> blends, Action renderManagers)
    {
        if (!shader.EnsureReady()) return false;
        if (!ReferenceEquals(inputOwner, shader))
        {
            inputOwner = shader;
            inputAdapter = shader is ILiquidPoolInputs inputs ? new(inputs) : null;
        }
        var current = managers.SelectMany(manager => ReadPools(manager)).ToHashSet();
        foreach (var old in geometry.Keys.Where(pool => !current.Contains(pool)).ToArray())
        { geometry[old].Dispose(); geometry.Remove(old); }
        foreach (var pool in current)
        {
            if (geometry.TryGetValue(pool, out var existing) && existing.IsCurrent) continue;
            var candidate = new EngineLiquidPoolGeometry(pool);
            existing?.Dispose(); geometry[pool] = candidate;
        }
        using var targets = new RenderPassTargets(pass);
        var description = new GraphicsPipelineDesc(shader.GraphicsIdentity!, EngineLiquidPoolGeometry.Layout,
            targets.Signature, DynamicPipelineState.Viewport, depthStencil: depth, blending: blends);
        if (pipeline is null || pipeline.Description != description || !ReferenceEquals(pipeline.Shader, shader)
            || pipeline.ExecutableRevision != shader.ExecutableRevision)
        {
            var candidate = new GraphicsPipeline(lifetime, description, shader);
            pipeline?.Dispose(); pipeline = candidate;
        }
        return GraphicsCommandContext.TryRun(shader.PassName, [pipeline], true, context =>
        {
            if (active is not null) throw new InvalidOperationException("Liquid pool submissions cannot nest.");
            context.BeginPass(pass);
            context.SetPipeline(pipeline);
            context.SetDynamicState(new() { Viewport = context.PassViewport });
            // The narrow input bridge stages retained values before Draw publishes them.
            var engine = (Vintagestory.Client.RenderAPIBase)api.Render;
            bool previous = LiquidMeshSource.UseSsbo(engine);
            commands = context; active = this;
            try { LiquidMeshSource.UseSsbo(engine) = false; renderManagers(); context.EndPass(); }
            finally { LiquidMeshSource.UseSsbo(engine) = previous; active = null; commands = null; }
        });
    }

    /// <summary>Returns the input-only bridge solely during the existing active submission lifetime.</summary>
    internal static IShaderProgram? ActiveInputs => active == null ? null : active.inputAdapter
        ?? throw new InvalidOperationException("The selected liquid program has no pool input sink.");

    /// <summary>Replaces only a currently declared VGE liquid pool draw; unrelated engine pools retain their original path.</summary>
    internal static bool TryDraw(MeshDataPool pool)
    {
        if (active is null) return false;
        if (!active.geometry.TryGetValue(pool, out var geometry))
            throw new InvalidOperationException("Engine submitted an unprepared liquid pool.");
        if (pool.indicesGroupsCount > 0) active.commands!.Draw(geometry, new(0, pool.indicesGroupsCount));
        return true;
    }

    /// <summary>Retires realizations and metadata without disposing any engine storage.</summary>
    public void Dispose()
    {
        unregister(); api.Event.LeaveWorld -= Invalidate;
        foreach (var entry in geometry.Values) entry.Dispose();
        geometry.Clear(); pipeline?.Dispose(); lifetime.Dispose(); borrowed?.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Withdraws borrowed identity after rebuild or world retirement, including reused native names.</summary>
    private void Invalidate() => dirty = true;
    #endregion
}
