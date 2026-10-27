using System;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains a fixed compute shader for application without owning or relinking that shader.</summary>
public sealed class RHIComputePipeline : IDisposable
{
    private readonly OpenGLBackend backend;
    private int lifetime;
    #region Public API
    /// <summary>Gets the immutable description used to realize this pipeline.</summary>
    public RHIComputePipelineDescription Description { get; }
    /// <summary>Reports retirement of this managed realization independently of its shader.</summary>
    public bool IsDisposed => Volatile.Read(ref lifetime) == 2;
    /// <summary>Retires only this managed realization; shader ownership remains with its original owner.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref lifetime, 2, 0) == 1)
            throw new InvalidOperationException("An executing compute pipeline cannot be retired.");
    }
    /// <summary>Supplies a consumer-only reference to this exact realization.</summary>
    public static implicit operator RHIComputePipelineRef(RHIComputePipeline? pipeline) => new(pipeline);
    #endregion

    #region Internal API
    /// <summary>Captures validated application data without another native allocation or registry entry.</summary>
    internal RHIComputePipeline(OpenGLBackend backend, RHIComputePipelineDescription description)
    { this.backend = backend; Description = description; }
    /// <summary>Requires the originating context, live realization and unchanged executable before application.</summary>
    internal RHIShader RequireShader()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        backend.RequireContext();
        return Description.Shader.RequireShader();
    }
    /// <summary>Protects this realization against retirement or recursive submission until execution returns.</summary>
    internal void BeginSubmission()
    {
        int previous = Interlocked.CompareExchange(ref lifetime, 1, 0);
        ObjectDisposedException.ThrowIf(previous == 2, this);
        if (previous != 0) throw new InvalidOperationException("The compute pipeline is already executing.");
    }
    /// <summary>Releases the active submission after all native callbacks and cleanup have returned.</summary>
    internal void EndSubmission() => Volatile.Write(ref lifetime, 0);
    #endregion
}
