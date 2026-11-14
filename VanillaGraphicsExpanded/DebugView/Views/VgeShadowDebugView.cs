using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Registers bounded shadow admission statistics and direct GPU whole-light status inspection.</summary>
public static partial class VgeBuiltInDebugViews
{
    private static string shadowView="readiness";
    #region Private
    /// <summary>Uses existing debug ownership; activation never enables the surface shadow provider.</summary>
    private static DebugViewDefinition CreateShadowView()=>new("vge.shadows","Virtual Shadows","Shadows",
        "GPU rows: current/stale/unavailable, detail, missing coverage, stale fraction, recovery. CPU values are admission estimates.",
        context=>new ShadowStatusRenderer(context.Capi),createPanel:_=>new ShadowPanel());

    /// <summary>Displays CPU-known work limits without triggering GPU observation from GUI callbacks.</summary>
    private sealed class ShadowPanel:DebugViewPanelBase
    {
        private GuiComposer? composer;
        private string? key;
        #region Public API
        /// <summary>Refreshes the currently open panel.</summary>
        public override bool WantsGameTick=>true;
        /// <summary>Composes the shared admission/capacity summary.</summary>
        public override void Compose(GuiComposer composer,ElementBounds bounds,string keyPrefix)
        {
            this.composer=composer;key=keyPrefix+"-shadow-stats";
            composer.AddDropDown(["readiness","candidates","pages"],["Whole-light readiness / age / duty cycle","Candidates: status / count / cells / slabs / unknown / guard / stage / probes","Pages: residency / invalidation / physical address / generation / source"],
                shadowView=="pages"?2:shadowView=="candidates"?1:0,(value,_)=>shadowView=value,ElementBounds.Fixed(0,0,480,30).WithParent(bounds),keyPrefix+"-mode")
                .AddDynamicText(ShadowDiagnostics.Text,CairoFont.WhiteSmallText(),ElementBounds.Fixed(0,42,480,340).WithParent(bounds),key);
        }
        /// <summary>Refreshes text from immutable CPU diagnostics only.</summary>
        public override void OnGameTick(float dt){if(key is not null)composer?.GetDynamicText(key)?.SetNewText(ShadowDiagnostics.Text);}
        #endregion
    }

    /// <summary>Draws GPU diagnostic records through the existing window submission owner.</summary>
    private sealed class ShadowStatusRenderer:IRenderer
    {
        private readonly ICoreClientAPI api;
        private readonly EngineFullscreenGeometry geometry;
        private readonly DebugGraphicsSubmission submission;
        #region Public API
        /// <summary>Draws after window composition in the debug-view interval.</summary>
        public double RenderOrder=>1;
        /// <summary>Diagnostics have no world-distance cutoff.</summary>
        public int RenderRange=>int.MaxValue;
        /// <summary>Creates shared fullscreen geometry and registers the diagnostic callback.</summary>
        internal ShadowStatusRenderer(ICoreClientAPI api)
        {
            this.api=api;submission=new(api);
            var quad=QuadMeshUtil.GetCustomQuadModelData(-1,-1,0,2,2);quad.Rgba=null;
            geometry=EngineFullscreenGeometry.Upload(api.Render,quad);
            api.Event.RegisterRenderer(this,EnumRenderStage.AfterBlit,"vge_shadow_status");
        }
        /// <summary>Binds current GPU records without CPU readback.</summary>
        public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
        {
            if(stage!=EnumRenderStage.AfterBlit || ShadowDiagnostics.Readiness?.Invoke() is not { } status)return;
            var shader=GpuShaderProgramLibraries.Get<ShadowReadinessDebugShader>(api,"shadows/readiness-debug");
            if(shader is null)return;
            shader.DisplayCandidates=shadowView=="candidates";shader.DisplayPages=shadowView=="pages";
            ((IShadowReadinessDebugBindings)shader).Status=status;
            ((IShadowReadinessDebugBindings)shader).Candidates=ShadowDiagnostics.CandidateHeaders?.Invoke()??status;
            ((IShadowReadinessDebugBindings)shader).Pages=ShadowDiagnostics.PageEntries?.Invoke()??status;
            ((IShadowReadinessDebugBindings)shader).SampleFailures=ShadowDiagnostics.SampleFailures?.Invoke()??status;
            // AfterBlit can precede the final Done callback. Order fragment
            // diagnostic writes before the overlay's read in either ordering.
            RHICommandContext.Current.Barrier(new(ResourceAccess.ShaderStorage));
            submission.DrawIndexed("Shadows.Debug",shader,geometry.Inputs,new(0,6),PrimitiveTopology.Triangles);
        }
        /// <summary>Ends callbacks and owned debug draws without taking shadow resource ownership.</summary>
        public void Dispose(){api.Event.UnregisterRenderer(this,EnumRenderStage.AfterBlit);geometry.Dispose();submission.Dispose();}
        #endregion
    }
    #endregion
}
