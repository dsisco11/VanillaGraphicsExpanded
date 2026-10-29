using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Closes uniform publication after the engine's rendering callbacks finish.</summary>
internal sealed class RHIUniformRingEndRenderer : IRenderer
{
    private const double RenderOrderValue = 1000.0;

    private readonly RHIUniformRingFrameController controller;

    #region Public API
    /// <summary>Retains the existing frame controller without taking allocator ownership.</summary>
    public RHIUniformRingEndRenderer(RHIUniformRingFrameController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => 0;

    /// <summary>Closes the allocation epoch at the engine's Done stage.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Done)
        {
            controller.EndFrame();
        }
    }

    /// <summary>Leaves allocator retirement to the owning frame controller.</summary>
    public void Dispose() { }
    #endregion
}
