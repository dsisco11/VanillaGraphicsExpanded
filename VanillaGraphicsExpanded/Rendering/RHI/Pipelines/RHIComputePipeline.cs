using System;
using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains fixed compute application data under its caller's resource lifetime contract.</summary>
public sealed class RHIComputePipeline : IDisposable
{
    private readonly IRHIContextLifetime origin;
    #region Public API
    /// <summary>Gets the immutable description established by pipeline creation.</summary>
    public RHIComputePipelineDescription Description { get; }
    /// <summary>Reports explicit owner retirement.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Retires managed application data; consumers must already have finished or been withdrawn.</summary>
    public void Dispose() { IsDisposed = true; }
    /// <summary>Supplies a non-owning reference to this exact realization.</summary>
    public static implicit operator RHIComputePipelineRef(RHIComputePipeline? pipeline) => new(pipeline);
    #endregion

    #region Internal API
    /// <summary>Captures established application data without another registry or native allocation.</summary>
    internal RHIComputePipeline(IRHIContextLifetime origin, RHIComputePipelineDescription description)
    { this.origin = origin; Description = description; }
    /// <summary>Admits the context and selects fixed data without per-dispatch metadata proof or resource guards.</summary>
    internal RHIShader RequireShader()
    {
        origin.RequireContext();
        ValidateLifetime();
        return Description.Shader.SelectedShader;
    }
    #endregion

    #region Private
    /// <summary>Diagnoses illegal owner retirement without becoming Release lifetime machinery.</summary>
    [Conditional("DEBUG")]
    private void ValidateLifetime()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(Description.Shader.SelectedShader.IsDisposed, Description.Shader.SelectedShader);
    }
    #endregion
}
