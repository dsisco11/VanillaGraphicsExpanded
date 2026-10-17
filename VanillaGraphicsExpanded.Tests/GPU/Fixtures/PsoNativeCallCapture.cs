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
        var apply = AccessTools.Method(typeof(StateCache), "ApplyGraphicsState", [typeof(GraphicsPipelineDesc), typeof(GraphicsDynamicState),
            typeof(GraphicsStateRelevance), typeof(PipelineStateCoverage)]);
        harmony.Patch(apply, prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Application))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Setup))));
        harmony.Patch(AccessTools.Method(typeof(StateCache), "RestoreBoundaryState"),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Restoration))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(PsoNativeCallCapture), nameof(Setup))));
    }

    /// <summary>Retires observers before any inspection or fixture cleanup.</summary>
    public void Dispose() { harmony.UnpatchAll(harmony.Id); active = null; }
    #endregion

    #region Private
    /// <summary>Labels the actual shared application, excluding fixture preparation and pass loads.</summary>
    private static void Application(GraphicsPipelineDesc pipeline)
    {
        if (active is not { } capture) return;
        capture.segment = "application";
        capture.Events.Add("draw:" + string.Join(",", pipeline.Blending.Select((blend, slot) =>
            pipeline.Targets.Colors[slot].Format is null ? "hole" : blend.Enabled ? "blend" : "replace")));
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
