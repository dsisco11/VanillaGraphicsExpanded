using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Opens uniform publication before the engine's rendering callbacks consume allocations.</summary>
internal sealed class RHIUniformRingBeginRenderer : IRenderer
{
    private const double RenderOrderValue = -1000.0;

    private readonly RHIUniformRingFrameController controller;

    #region Public API
    /// <summary>Retains the existing frame controller without taking allocator ownership.</summary>
    public RHIUniformRingBeginRenderer(RHIUniformRingFrameController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => 0;

    /// <summary>Advances the allocation epoch at the engine's Before stage.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Before)
        {
            controller.BeginFrame();
        }
    }

    /// <summary>Leaves allocator retirement to the owning frame controller.</summary>
    public void Dispose() { }
    #endregion
}
