using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Retains fixed pipeline preparation and pure view-pass intentions without holding an active attachment borrow.</summary>
internal sealed class CloudVolumeViewPass(GpuProgram program) : IDisposable
{
    #region Internal API
    /// <summary>Gets the most recently accepted immutable attachment/surface intentions.</summary>
    internal RHIRenderPassDescription? Description { get; private set; }
    /// <summary>Gets the exact fixed executable realization admitted for these target formats.</summary>
    internal RHIGraphicsPipeline? Pipeline => asset?.Pipeline;
    private GraphicsPipelineAsset? asset;
#if DEBUG
    /// <summary>Counts successful signature preparations independently of drawing.</summary>
    internal int Preparations { get; private set; }
#endif
    /// <summary>Resolves published compatibility only, then installs a fully validated replacement without target realization or readback.</summary>
    internal void Prepare(RHIRenderPassDescription pass, bool blend)
    {
        var context = RHICommandContext.Current;
        var signature = context.DescribeRenderTargets(pass);
        var description = new RHIGraphicsPipelineDescription(program.Shader, CloudVolumeViewRenderer.Layout, signature,
            DynamicPipelineState.Viewport, blending: blend ? LiquidPipelineStates.SurfaceBlending : null);
        if (Pipeline is { } current && current.Description == description)
        { Description = pass; return; }
        var replacement = new GraphicsPipelineAsset(program,description, shader => DescribeCandidate(shader,description));
        var old = asset; asset = replacement; Description = pass;
        old?.Dispose();
#if DEBUG
        Preparations++;
#endif
    }
    /// <summary>Submits the frozen pass and captured typed inputs through the unified context using explicit nonindexed geometry.</summary>
    internal void Draw(RHICommandContext commands, RHIGeometryInputs geometry)
    {
        commands.BeginRenderPass(Description ?? throw new InvalidOperationException("Stored cloud pass is not prepared."));
        commands.SetPipeline(Pipeline ?? throw new InvalidOperationException("Stored cloud pipeline is not prepared."));
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.BindResources(program.CaptureBindings()); commands.Draw(geometry, new(0, 3)); commands.EndRenderPass();
    }
    #endregion

    #region Private
    /// <summary>Authors the temporal diagnostic output contract from the selected option before shader installation.</summary>
    private RHIGraphicsPipelineDescription DescribeCandidate(RHIShaderRef shader, RHIGraphicsPipelineDescription previous)
    {
        var targets = previous.Targets;
        if (program is CloudVolumeTemporalProgram temporal)
        {
            TextureFormat[] formats = temporal.DiagnosticsEnabled
                ? [TextureFormat.Rgba32f, TextureFormat.Rgb32f, TextureFormat.Rgba32f, TextureFormat.R16f, TextureFormat.Rgba16f]
                : [TextureFormat.Rgba32f, TextureFormat.Rgb32f, TextureFormat.Rgba32f, TextureFormat.R16f];
            targets = new(System.Linq.Enumerable.Select(formats, format => new ColorTargetSlot(format)));
        }
        return new(shader, previous.VertexLayout, targets, previous.Dynamics, blending: program is CloudVolumeTemporalProgram ? null : previous.Blending);
    }
    #endregion

    #region Public API
    /// <summary>Retires only the managed pipeline; shader and image owners retain their independent lifetimes.</summary>
    public void Dispose()
    { asset?.Dispose(); asset = null; Description = null; }
    #endregion
}
