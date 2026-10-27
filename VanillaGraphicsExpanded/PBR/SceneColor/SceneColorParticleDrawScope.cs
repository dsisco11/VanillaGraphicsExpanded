using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Redirects one engine particle submission and restores its documented opaque draw boundary.</summary>
internal sealed class SceneColorParticleDrawScope : IDisposable
{
    /// <summary>Accumulates premultiplied particle radiance and coverage without changing borrowed glow blending.</summary>
    internal static readonly GlPipelineDesc CapturePipeline = new(
        defaultMask: default,
        nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed)
            .With(GlPipelineStateId.BlendFuncIndexed),
        blendEnableIndexedAttachments: [0],
        blendFuncIndexed: [new(0, new(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha))],
        name: "SceneColor.Particles.Capture");
    private static readonly GlPipelineDesc ParticleBoundary = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable),
        nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed)
            .With(GlPipelineStateId.BlendFuncIndexed),
        blendEnableIndexedAttachments: [0],
        blendFuncIndexed: [new(0, new(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha))],
        name: "SceneColor.Particles.EngineBoundary");
    private readonly SceneColorParticleTargets targets;
    private readonly IDisposable? bindings;
    private bool completed;
    private bool disposed;

    #region Public API
    /// <summary>Captures initial depth, then admits engine particle routing under backend-owned restoration.</summary>
    internal SceneColorParticleDrawScope(SceneColorParticleTargets targets)
    {
        this.targets = targets;
        try
        {
            targets.BeginCapture();
            bindings = OpenGLRHIContext.BeginEngineParticleCapture(targets.DrawTarget, CapturePipeline, ParticleBoundary);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Captures final visibility only after the original particle submission returns successfully.</summary>
    internal void Complete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        targets.EndCapture();
        completed = true;
    }

    /// <summary>Restores original routing and the engine particle blend contract on success or exception.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!completed) targets.ResetCapture();
        bindings?.Dispose();
    }
    #endregion
}
