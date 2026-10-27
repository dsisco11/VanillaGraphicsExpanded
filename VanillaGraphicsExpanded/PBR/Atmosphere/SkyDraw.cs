using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Retains one prepared sky pass's footprint, submission and independent failure policy.</summary>
internal sealed class SkyDraw
{
    private readonly Action<RHICommandContext> draw;
    private readonly Action<Exception> failed;
    internal string Name { get; }
    internal RHIGraphicsPipeline[] Pipelines { get; }

    #region Public API
    /// <summary>Validates prepared executables before the renderer enters graphics authority.</summary>
    internal SkyDraw(string name, RHIGraphicsPipeline[] pipelines, Action<RHICommandContext> draw,
        Action<Exception> failed)
    {
        Name = name;
        Pipelines = pipelines.ToArray();
        foreach (var pipeline in Pipelines) pipeline.RequireShader();
        this.draw = draw;
        this.failed = failed;
    }

    /// <summary>Draws all admitted sky passes within one declared graphics boundary.</summary>
    internal static void Run(IReadOnlyList<SkyDraw> draws)
    {
        if (draws.Count == 0) return;
        bool entered = false;
        try
        {
            // All pass preparation is complete, so the resource footprint is known before entry.
            var pipelines = draws.SelectMany(item => item.Pipelines).Distinct().ToArray();
            var commands=RHICommandContext.Current;
            if (commands.TryEnterBoundary(new("Sky",true,graphics:pipelines.Select(value=>(RHIGraphicsPipelineRef)value),operations:BoundaryOperations.RenderPasses),out var boundary))
            {
                using (boundary) boundary!.Run(()=>
                {
                entered = true;
                foreach (var item in draws) item.Execute(commands);
                });
                return;
            }
        }
        catch (Exception error) when (!entered && !EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            foreach (var item in draws) item.failed(error);
            return;
        }
        foreach (var item in draws)
            item.failed(new InvalidOperationException("Owned sky graphics boundary was rejected."));
    }

    /// <summary>Ends a failed pass's image authority before allowing the next sky pass to draw.</summary>
    internal void Execute(RHICommandContext commands)
    {
        using var group = GlDebug.Group(Name);
        try { draw(commands); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            if (commands.HasRenderPass) commands.EndRenderPass();
            failed(error);
        }
    }
    #endregion
}
