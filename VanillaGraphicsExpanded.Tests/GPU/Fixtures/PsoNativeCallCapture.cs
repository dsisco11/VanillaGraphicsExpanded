using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Records a bounded family of emitted GL wrappers, separately from setup, draw application and engine restoration.</summary>
internal sealed class PsoNativeCallCapture : IDisposable
{
    [ThreadStatic] private static PsoNativeCallCapture? active;
    private readonly Harmony harmony = new("Tests.PsoNativeCallCapture");
    private string segment = "setup";
    internal readonly List<string> Events = [];

    #region Public API
    /// <summary>Installs exact wrapper observers and non-mutating operation markers before measurement begins.</summary>
    internal PsoNativeCallCapture()
    {
        active = this;
        string[] names = ["Enable", "Disable", "LineWidth", "PointSize", "Scissor", "BlendFuncSeparate", "BlendEquationSeparate",
            "BlendColor", "ColorMask", "DepthFunc", "DepthMask", "StencilFuncSeparate", "StencilOpSeparate", "StencilMaskSeparate",
            "PolygonMode", "PolygonOffset", "CullFace", "FrontFace", "Viewport", "SampleCoverage", "SampleMask", "MinSampleShading",
            "PrimitiveRestartIndex", "PatchParameter", "PointParameter", "ProvokingVertex", "DepthRange"];
        foreach (var method in typeof(GL).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => names.Contains(method.Name) && !method.IsGenericMethod && method.GetParameters().All(p =>
                !p.ParameterType.IsPointer && !p.ParameterType.IsByRef && !p.ParameterType.IsArray && p.ParameterType != typeof(uint))))
            harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Observe))));
        // Observe the typed draw because optimized state helpers may already be inlined.
        var apply = AccessTools.Method(typeof(RHICommandContext), "Draw");
        harmony.Patch(apply, prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Application))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(DrawCompleted))));
        harmony.Patch(AccessTools.Method(typeof(StateCache), "RestoreBoundaryState"),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Restoration))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Setup))));
    }

    /// <summary>Retires observers before any inspection or fixture cleanup.</summary>
    public void Dispose() { harmony.UnpatchAll(harmony.Id); active = null; }
    #endregion

    #region Private
    /// <summary>Labels the complete typed draw, excluding fixture preparation and pass loads.</summary>
    private static void Application(RHIGraphicsPipeline? ___pipeline)
    {
        if (active is not { } capture || ___pipeline is null) return;
        var pipeline=___pipeline.Description;
        capture.segment = "application";
        capture.Events.Add("draw:" + string.Join(",", pipeline.Blending.Select((blend, slot) =>
            pipeline.Targets.Colors[slot].Format is null ? "hole" : blend.Enabled ? "blend" : "replace")));
    }
    /// <summary>Verifies actual active blend factors before boundary handback, independent of inlined GL wrappers.</summary>
    private static void DrawCompleted(RHIGraphicsPipeline? ___pipeline)
    {
        if(active is {} capture && ___pipeline is {} pipeline && pipeline.Description.Blending.Any(blend=>blend.Enabled))
        {
            // Reuse the native fingerprint reader; wrapper interception alone can miss JIT-inlined calls.
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
    /// <summary>Records the selected wrapper and its enum or scalar arguments without suppressing native execution.</summary>
    private static void Observe(MethodBase __originalMethod, object[] __args)
    {
        if (active is { } capture) capture.Events.Add(capture.segment + ":" + __originalMethod.Name + "(" + string.Join(",", __args) + ")");
    }
    #endregion
}
