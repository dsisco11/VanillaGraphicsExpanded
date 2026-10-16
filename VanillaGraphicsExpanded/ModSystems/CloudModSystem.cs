using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Composes native cloud adaptation; shader/draw/history resources belong to their cloud-domain owners.</summary>
public sealed class CloudModSystem : ModSystem, IRenderer
{
    private ICoreClientAPI? api;
    private CloudNativeRuntime? runtime;

    #region Public API
    /// <summary>Runs after the shared camera is published and before cloud generation.</summary>
    public double RenderOrder => .002;
    /// <summary>Ownership maintenance does not depend on visible chunks.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Cloud GPU input adaptation is client-only and independent of LumOn.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    /// <summary>Registers cloud input maintenance and preparation before frozen physical lighting admission.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api; runtime = new(api);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_cloud_inputs");
    }

    /// <summary>Prepares owned resources; native callbacks retain weather ticking and visible draw timing.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Before) { runtime?.Update(); runtime?.PrepareSimple(); runtime?.PrepareVolume(); }
    }

    /// <summary>Unregisters VGE callbacks and releases only its own lifetime.</summary>
    public override void Dispose()
    {
        var client = api; api = null;
        var owner = runtime; runtime = null;
        RenderingCleanup.Run(
            () => client?.Event.UnregisterRenderer(this, EnumRenderStage.Before),
            () => owner?.Dispose(),
            () => base.Dispose());
    }
    #endregion
}
