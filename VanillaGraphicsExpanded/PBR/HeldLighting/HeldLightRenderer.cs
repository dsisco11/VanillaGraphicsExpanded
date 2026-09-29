using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Resolves reserved held-light positions after the engine's Before-stage animation update at 0.4.</summary>
internal sealed class HeldLightRenderer : IRenderer
{
    private bool disposed;
    public double RenderOrder => 0.45;
    public int RenderRange => int.MaxValue;

    #region Renderer lifecycle
    /// <summary>Updates only held-light positions; the engine retains ownership of collection and all other lights.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!disposed && stage == EnumRenderStage.Before) HeldLightSystem.Complete();
    }

    /// <summary>Disables callbacks already present in an engine dispatch snapshot.</summary>
    public void Dispose() => disposed = true;
    #endregion
}
