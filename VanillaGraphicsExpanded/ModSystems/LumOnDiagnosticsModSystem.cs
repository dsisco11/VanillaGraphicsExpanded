using System;

using VanillaGraphicsExpanded.LumOn.Scene;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>
/// Diagnostics-only facade for querying LumOn runtime state from debug views.
/// Keeps debug-specific APIs out of the core LumOnModSystem.
/// </summary>
public sealed class LumOnDiagnosticsModSystem : ModSystem
{
    private ICoreClientAPI? capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
    }

    internal int CopyLumonSceneNearRegionDebugSnapshots(Span<LumonSceneRegionCellDebugSnapshot> dst)
    {
        try
        {
            if (capi is null)
            {
                return 0;
            }

            if (!ConfigModSystem.Config.LumOn.Enabled || !ConfigModSystem.Config.LumOn.LumonScene.Enabled)
            {
                return 0;
            }

            LumOnModSystem lumOn = capi.ModLoader.GetModSystem<LumOnModSystem>();
            LumonSceneFeedbackUpdateRenderer? feedback = lumOn.GetLumonSceneFeedbackUpdateRendererOrNull();
            return feedback is null ? 0 : feedback.CopyNearRegionDebugSnapshots(dst);
        }
        catch
        {
            return 0;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        capi = null;
    }
}
