using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Owns a renderer's pipeline contract and installed fixed realization across coherent shader reloads.</summary>
internal sealed class GraphicsPipelineAsset : IDisposable
{
    private readonly GpuProgram owner;
    private readonly Func<RHIShaderRef, RHIGraphicsPipelineDescription> describe;
    private RHIGraphicsPipeline pipeline;
    private bool disposed;

    #region Public API
    /// <summary>Unregisters this renderer contract and retires its realization without disposing the shader.</summary>
    public void Dispose()
    {
        if (disposed) return;
        owner.RequireOutsideSubmission();
        pipeline.Dispose();
        disposed = true;
        owner.UnregisterPipeline(this);
    }
    #endregion

    #region Internal API
    /// <summary>Gets one fixed realization; capturing it never follows a later asset installation.</summary>
    internal RHIGraphicsPipeline Pipeline
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return pipeline; }
    }
    /// <summary>Gets the installed behavior for renderer-local reuse decisions.</summary>
    internal RHIGraphicsPipelineDescription Description => Pipeline.Description;
    /// <summary>Builds a validated renderer contract before registering it with its shader owner.</summary>
    internal GraphicsPipelineAsset(GpuProgram owner, RHIGraphicsPipelineDescription description,
        Func<RHIShaderRef, RHIGraphicsPipelineDescription>? describe = null)
    {
        this.owner = owner;
        owner.RequireOutsideSubmission();
        if (description.Shader != owner.Shader)
            throw new ArgumentException("A renderer pipeline must select its owner's installed shader.", nameof(description));
        this.describe = describe ?? (shader => new(shader, description.VertexLayout, description.Targets,
            description.Dynamics, description.DepthStencil, description.Rasterizer, description.Blending,
            description.Sampling, description.Assembly, description.Output, description.Label));
        pipeline = RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource();
        owner.RegisterPipeline(this);
    }
    /// <summary>Builds an unpublished replacement using the renderer's authored contract and the exact shader candidate.</summary>
    internal RHIGraphicsPipeline PrepareReplacement(RHIShaderRef shader)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var description = describe(shader);
        if (description.Shader != shader)
            throw new InvalidOperationException("A pipeline recipe must use the supplied fixed shader candidate.");
        return RHICommandContext.Current.CreateGraphicsPipeline(description).RequireResource();
    }
    /// <summary>Publishes an already validated replacement during the shader owner's nonthrowing installation step.</summary>
    internal RHIGraphicsPipeline Install(RHIGraphicsPipeline replacement)
    {
        var previous = pipeline;
        pipeline = replacement;
        return previous;
    }
    #endregion
}
