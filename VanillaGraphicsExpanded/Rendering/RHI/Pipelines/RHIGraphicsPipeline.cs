using System;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns immutable graphics application data for one fixed executable on its original context.</summary>
public sealed class RHIGraphicsPipeline : IDisposable
{
    private readonly OpenGLBackend backend;
    private int lifetime;

    #region Public API
    /// <summary>Gets the frozen behavior and exact shader generation selected at creation.</summary>
    public RHIGraphicsPipelineDescription Description { get; }
    /// <summary>Reports retirement independently of the shader's owning asset.</summary>
    public bool IsDisposed => Volatile.Read(ref lifetime) == 2;
    /// <summary>Retires this managed realization without acquiring shader disposal authority.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref lifetime, 2, 0) == 1)
            throw new InvalidOperationException("An executing graphics pipeline cannot be retired.");
    }
    /// <summary>Retains this exact realization through a consumer-only reference.</summary>
    public static implicit operator RHIGraphicsPipelineRef(RHIGraphicsPipeline? pipeline) => new(pipeline);
    #endregion

    #region Internal API
    /// <summary>Gets the executable's immutable resource interface without another preparation pass.</summary>
    internal GpuPreparedBindings Bindings { get; }
    /// <summary>Gets the prepared native relevance used by application and selective restoration.</summary>
    internal GraphicsStateRelevance Relevance { get; }
    /// <summary>Gets the complete potential state footprint admitted before execution.</summary>
    internal PipelineStateCoverage Coverage { get; }
    /// <summary>Publishes application data only after the backend validates every executable and state dependency.</summary>
    internal RHIGraphicsPipeline(OpenGLBackend backend, RHIGraphicsPipelineDescription description,
        GpuPreparedBindings bindings, GraphicsStateRelevance relevance)
    {
        this.backend = backend; Description = description; Bindings = bindings;
        Relevance = relevance; Coverage = PipelineStateCoverage.From(relevance);
    }
    /// <summary>Rejects retired, changed or wrong-context dependencies without consulting a mutable asset owner.</summary>
    internal RHIShader RequireShader()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        backend.RequireContext();
        return Description.Shader.RequireShader();
    }
    /// <summary>Requires an exact format/aspect/sample contract while excluding framebuffer identity and dimensions.</summary>
    internal void ValidateTargets(RenderTargetSignature targets)
    {
        _ = RequireShader();
        if (Description.Targets != targets) throw new InvalidOperationException("Incompatible graphics target signature.");
    }
    /// <summary>Rejects recursive use and protects the realization against retirement until execution returns.</summary>
    internal void BeginSubmission()
    {
        int previous = Interlocked.CompareExchange(ref lifetime, 1, 0);
        ObjectDisposedException.ThrowIf(previous == 2, this);
        if (previous != 0) throw new InvalidOperationException("The graphics pipeline is already executing.");
    }
    /// <summary>Releases execution authority after callbacks and native cleanup have returned.</summary>
    internal void EndSubmission() => Volatile.Write(ref lifetime, 0);
    #endregion
}
