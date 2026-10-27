using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Captures typed desired inputs without executing commands or following replacement generations.</summary>
public abstract partial class GpuProgram
{
    #region Public API
    /// <summary>Captures the installed shader's complete input set with no native context or allocation requirement.</summary>
    public RHIResourceBindings CaptureBindings()
    {
        lifetime.RequireMutation(); lifetime.RequireSubmissionAdmission();
        if (Shader.IsEmpty) throw new InvalidOperationException("Prepare a graphics executable before capturing bindings.");
        lifetime.IsPublishing = true;
        try { return CaptureBindingsCore(); }
        finally { lifetime.IsPublishing = false; }
    }
    #endregion
    #region Internal API
    /// <summary>Rejects executable work while desired inputs are being captured.</summary>
    internal void RequireOutsideSubmission()
    {
        lifetime.RequireOutsidePublication();
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
    }
    #endregion
    #region Protected API
    /// <summary>Rejects retained-input writes after retirement or during capture.</summary>
    protected internal void RequireInputMutation() => lifetime.RequireMutation();
    /// <summary>Freezes authored values through the compiled binding contract without publishing them.</summary>
    protected abstract RHIResourceBindings CaptureBindingsCore();
    #endregion
    #region Private
    /// <summary>Shares the asset lifetime with preparation and retirement policy.</summary>
    GpuProgramLifetime IGpuProgram.Lifetime => lifetime;
    #endregion
}
