using System;
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
    private readonly StateCache.FramebufferScope bindings;
    private bool completed;
    private bool disposed;

    #region Public API
    /// <summary>Establishes the engine's full-scene particle contract before preserving cached copy state.</summary>
    internal SceneColorParticleDrawScope(SceneColorParticleTargets targets)
    {
        this.targets = targets;
        var state = StateCache.Current;
        bindings = state.BindFramebufferScope();
        try
        {
            // The installed OnRenderFrame3D sets standard source-alpha blending
            // immediately before Render(1). This scope owns only output zero and
            // full-scene scissor intent; borrowed glow retains its engine factors.
            state.Apply(ParticleBoundary);
            targets.BeginCapture();
            targets.DrawTarget.Bind();
            state.Apply(CapturePipeline);
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
        try { bindings.Dispose(); }
        finally { StateCache.Current.Apply(ParticleBoundary); }
    }
    #endregion
}
