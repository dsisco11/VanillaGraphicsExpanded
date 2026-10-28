using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Records native state effects separately from setup, draw application and engine restoration.</summary>
internal sealed partial class PsoNativeCallCapture : IDisposable
{
    [ThreadStatic] private static PsoNativeCallCapture? active;
    private readonly Harmony harmony = new("Tests.PsoNativeCallCapture");
    private string segment = "setup";
    internal readonly List<string> Events = [];

    #region Public API
    /// <summary>Installs typed native observers and managed operation markers before workload execution.</summary>
    internal PsoNativeCallCapture()
    {
        active = this;
        // Observe the typed draw because optimized state helpers may already be inlined.
        var apply = AccessTools.Method(typeof(OpenGLBackend), "Draw");
        harmony.Patch(apply, prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Application))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(DrawCompleted))));
        harmony.Patch(AccessTools.Method(typeof(OpenGlStateCache), "RestoreBoundaryState"),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Restoration))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Setup))));
        InstallNativeObservers();
    }

    /// <summary>Retires observers before any inspection or fixture cleanup.</summary>
    public void Dispose()
    {
        try { harmony.UnpatchAll(harmony.Id); }
        finally { active = null; native.Dispose(); }
    }
    #endregion

    #region Private
    /// <summary>Labels the complete typed draw, excluding fixture preparation and pass loads.</summary>
    private static void Application(RHIGraphicsPipelineRef reference)
    {
        if (active is not { } capture) return;
        var pipeline=reference.RequirePipeline().Description;
        capture.segment = "application";
        capture.Events.Add("draw:" + string.Join(",", pipeline.Blending.Select((blend, slot) =>
            pipeline.Targets.Colors[slot].Format is null ? "hole" : blend.Enabled ? "blend" : "replace")));
    }
    /// <summary>Verifies actual active blend factors before boundary handback, independent of inlined GL wrappers.</summary>
    private static void DrawCompleted(RHIGraphicsPipelineRef reference)
    {
        if(active is {} capture && reference.RequirePipeline() is {} pipeline && pipeline.Description.Blending.Any(blend=>blend.Enabled))
        {
            // Check native values independently of the emitted-call log.
            var state=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(PsoNativeStateFingerprint.Read())!;
            for(int slot=0;slot<pipeline.Description.Blending.Count;slot++)
            {
                var blend=pipeline.Description.Blending[slot];
                if(!blend.Enabled || pipeline.Description.Targets.Colors[slot].Format is null)continue;
                Assert.Equal(((int)OpenGLBlendValues.SourceFactor(blend.SourceRgb)).ToString(),state["BlendSrcRgb:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.DestinationFactor(blend.DestinationRgb)).ToString(),state["BlendDstRgb:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.SourceFactor(blend.SourceAlpha)).ToString(),state["BlendSrcAlpha:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.DestinationFactor(blend.DestinationAlpha)).ToString(),state["BlendDstAlpha:"+slot]);
            }
            capture.Events.Add("validated:active-blend-factors");
        }
        Setup();
    }
    /// <summary>Labels work outside fixed-state application, including pass loads.</summary>
    private static void Setup() { if (active is { } capture) capture.segment = "setup"; }
    /// <summary>Labels boundary handback independently of draw setters.</summary>
    private static void Restoration() { if (active is { } capture) capture.segment = "restoration"; }
    #endregion
}
