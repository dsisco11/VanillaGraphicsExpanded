using System;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    #region Cloud lighting volume
    /// <summary>Registers candidate coverage and exact validity inspection in the existing debug menu.</summary>
    private static DebugViewDefinition CreateLightingVolumeView()=>new(
        "vge.cloud.lightingVolume","Cloud Lighting Volume",CategoryPbr,
        "Native density support, lighting coverage, validity/fallback and bounded update statistics.",
        _=>{LightingVolumeDiagnostics.Mode=2;return new ActionDisposable(()=>LightingVolumeDiagnostics.Mode=0);},
        availability:ctx=>ctx.Config.Clouds.VolumeRenderer==CloudVolumeRendererKind.LightingVolumeCandidate
            ? DebugViewAvailability.Available():DebugViewAvailability.Unavailable("Select the lighting-volume candidate first."),
        createPanel:_=>new LightingVolumePanel());

    /// <summary>Displays existing GPU scheduler records without creating another diagnostic resource owner.</summary>
    private sealed class LightingVolumePanel:DebugViewPanelBase
    {
        private GuiComposer? composer;
        private string? textKey;
        private float elapsed;
        #region Public API
        /// <summary>Refreshes text only while the panel is open.</summary>
        public override bool WantsGameTick=>true;
        /// <summary>Offers coverage and exact footprint visualizers alongside age/backlog/allocation statistics.</summary>
        public override void Compose(GuiComposer composer,ElementBounds bounds,string keyPrefix)
        {
            this.composer=composer;textKey=keyPrefix+"-stats";
            composer.AddDropDown(["1","2"],["Density / lighting coverage","Validity / fallback"],
                Math.Max(0,LightingVolumeDiagnostics.Mode-1),(value,_)=>LightingVolumeDiagnostics.Mode=int.Parse(value),
                ElementBounds.Fixed(0,0,420,30).WithParent(bounds),keyPrefix+"-mode")
                .AddDynamicText(CloudNativeRuntime.VolumeDiagnosticText,CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(0,42,460,300).WithParent(bounds),textKey);
        }
        /// <summary>Updates the already composed text without rebuilding UI or issuing GPU reads from the GUI tick.</summary>
        public override void OnGameTick(float dt)
        {
            elapsed+=dt;if(elapsed<1) return;elapsed=0;
            if(textKey is not null) composer?.GetDynamicText(textKey)?.SetNewText(CloudNativeRuntime.VolumeDiagnosticText);
        }
        #endregion
    }
    #endregion
}
