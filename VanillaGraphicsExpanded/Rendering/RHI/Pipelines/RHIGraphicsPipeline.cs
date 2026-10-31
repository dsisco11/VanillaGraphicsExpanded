using System;
using System.Diagnostics;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns immutable graphics application data for one fixed executable on its original context.</summary>
public sealed class RHIGraphicsPipeline : IDisposable
{
    private readonly IRHIContextLifetime origin;

    #region Public API
    /// <summary>Gets the frozen behavior and exact shader generation selected at creation.</summary>
    public RHIGraphicsPipelineDescription Description { get; }
    /// <summary>Reports retirement independently of the shader's owning asset.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Retires managed data after the owner has finished or withdrawn its consumers.</summary>
    public void Dispose() { IsDisposed = true; }    /// <summary>Retains this exact realization through a consumer-only reference.</summary>
    public static implicit operator RHIGraphicsPipelineRef(RHIGraphicsPipeline? pipeline) => new(pipeline);
    #endregion

    #region Internal API
    /// <summary>Gets opaque backend application data without native policy on the semantic object.</summary>
    internal IRHIGraphicsPipelineRealization Realization { get; }
    /// <summary>Publishes application data only after the backend validates every executable and state dependency.</summary>
    internal RHIGraphicsPipeline(IRHIContextLifetime origin, RHIGraphicsPipelineDescription description,
        IRHIGraphicsPipelineRealization realization)
    {
        this.origin = origin; Description = description;
        Realization = realization;
    }
    /// <summary>Rejects retired, changed or wrong-context dependencies without consulting a mutable asset owner.</summary>
    internal RHIShader RequireShader()
    {
        ValidateLifetime();
        origin.RequireContext();
        var shader = Description.Shader.SelectedShader;
        shader.RequirePrepared();
        return shader;
    }
    /// <summary>Requires an exact format/aspect/sample contract while excluding framebuffer identity and dimensions.</summary>
    internal void ValidateTargets(RenderTargetSignature targets)
    {
        _ = RequireShader();
        if (Description.Targets != targets) throw new InvalidOperationException("Incompatible graphics target signature.");
    }
    #endregion

    #region Private
    /// <summary>Diagnoses illegal retirement without providing consumer lifetime exclusion.</summary>
    [Conditional("DEBUG")]
    private void ValidateLifetime()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(Description.Shader.SelectedShader.IsDisposed, Description.Shader.SelectedShader);
    }
    #endregion
}
