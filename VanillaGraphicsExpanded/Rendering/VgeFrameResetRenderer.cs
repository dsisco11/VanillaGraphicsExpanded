using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Withdraws the shared frame before native camera preparation without owning another snapshot.</summary>
internal sealed class VgeFrameResetRenderer : IRenderer
{
    private readonly VgeFrameRenderer owner;

    #region Public API
    /// <summary>Retains the shared camera owner for the early frame boundary.</summary>
    internal VgeFrameResetRenderer(VgeFrameRenderer owner) => this.owner = owner;

    /// <summary>Runs before native camera and ambient callbacks.</summary>
    public double RenderOrder => -10001;
    /// <summary>Frame withdrawal is independent of visibility distance.</summary>
    public int RenderRange => int.MaxValue;

    /// <summary>Starts the shared frame lifetime without reading camera data.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Before) owner.BeginFrame();
    }

    /// <summary>Leaves snapshot retirement and callback withdrawal to the shared owner.</summary>
    public void Dispose() { }
    #endregion
}
