using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

/// <summary>Supplies cached procedural names for engine render stages, callbacks and fullscreen passes.</summary>
internal static class EngineRenderScopes
{
    private static readonly Dictionary<EnumRenderStage, string> Stages = CreateStageNames();
    private static readonly ConditionalWeakTable<RenderHandler, string> Handlers = new();
    private static readonly ConditionalWeakTable<ShaderProgramBase, string> Programs = new();

    #region Scope names
    /// <summary>Builds names for every engine stage without maintaining a stage allowlist.</summary>
    private static Dictionary<EnumRenderStage, string> CreateStageNames()
    {
        var names = new Dictionary<EnumRenderStage, string>();
        foreach (var stage in Enum.GetValues<EnumRenderStage>()) names[stage] = $"VS.{stage}";
        return names;
    }

    /// <summary>Returns the cached name of a dispatched render stage.</summary>
    internal static string StageName(EnumRenderStage stage) => Stages.TryGetValue(stage, out var name) ? name : "VS.UnknownStage";

    /// <summary>Uses the registration label alone; unnamed callbacks fall back to a short implementation name.</summary>
    internal static string HandlerName(RenderHandler handler) => Handlers.GetValue(handler, static value =>
    {
        // The enclosing stage already identifies engine dispatch; avoid repeating namespaces and delegate details.
        if (!string.IsNullOrWhiteSpace(value.ProfilingName)) return value.ProfilingName;
        if (value.Renderer is DummyRenderer dummy)
            return $"{dummy.action.Method.DeclaringType?.Name}.{dummy.action.Method.Name}";
        return value.Renderer.GetType().Name;
    });

    /// <summary>Identifies the current fullscreen shader without querying driver state.</summary>
    internal static string FullscreenName() => StateCache.ActiveProgram is Shaders.GpuProgram owned
        ? $"{owned.AssetDomain}:{owned.PassName}"
        : ShaderProgramBase.CurrentShaderProgram is { } program
        ? Programs.GetValue(program, static value => $"{value.AssetDomain ?? "game"}:{value.PassName ?? value.GetType().Name}")
        : "Fullscreen";
    #endregion

    #region Callback dispatch
    /// <summary>Balances the callback group even when the renderer throws; engine error handling remains outside it.</summary>
    internal static void Render(RenderHandler handler, float dt, EnumRenderStage stage)
    {
        using var scope = GlDebug.Group(HandlerName(handler));
        handler.Renderer.OnRenderFrame(dt, stage);
    }
    #endregion
}
