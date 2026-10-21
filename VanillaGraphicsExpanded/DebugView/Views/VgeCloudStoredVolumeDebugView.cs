using System;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    #region Cloud lighting volume
    /// <summary>Registers cloud coverage and exact validity inspection in the existing debug menu.</summary>
    private static DebugViewDefinition CreateStoredVolumeView()=>new(
        "vge.cloud.storedVolume","Cloud Stored Volume",CategoryPbr,
        "Native density support, lighting coverage, validity/fallback and bounded update statistics.",
        _=>{CloudStoredVolumeDiagnostics.Mode=4;return new ActionDisposable(()=>CloudStoredVolumeDiagnostics.Mode=0);},
        createPanel:_=>new StoredVolumePanel());

    /// <summary>Displays existing GPU scheduler records without creating another diagnostic resource owner.</summary>
    private sealed class StoredVolumePanel:DebugViewPanelBase
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
            composer.AddDropDown(["1","2","3","4","5","6","7","8"],["Stored density","Region identity","Completed age","Initialized fallback",
                "Temporal: fresh / carried / fallback","Temporal: effective prior weight","Temporal: rejection reason","Temporal: observation age"],
                Math.Max(0,CloudStoredVolumeDiagnostics.Mode-1),(value,_)=>CloudStoredVolumeDiagnostics.Mode=int.Parse(value),
                ElementBounds.Fixed(0,0,420,30).WithParent(bounds),keyPrefix+"-mode")
                .AddDynamicText(CloudNativeRuntime.StoredVolumeDiagnosticText,CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(0,42,460,300).WithParent(bounds),textKey);
        }
        /// <summary>Updates the already composed text without rebuilding UI or issuing GPU reads from the GUI tick.</summary>
        public override void OnGameTick(float dt)
        {
            elapsed+=dt;if(elapsed<1) return;elapsed=0;
            if(textKey is not null) composer?.GetDynamicText(textKey)?.SetNewText(CloudNativeRuntime.StoredVolumeDiagnosticText);
        }
        #endregion
    }
    #endregion
}
