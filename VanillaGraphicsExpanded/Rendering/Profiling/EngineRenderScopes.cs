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

    /// <summary>Retains registration names, including action callbacks represented by DummyRenderer.</summary>
    internal static string HandlerName(RenderHandler handler) => Handlers.GetValue(handler, static value =>
        $"VS.Renderer.{value.ProfilingName}.{(value.Renderer is DummyRenderer dummy ? $"{dummy.action.Method.DeclaringType?.FullName}.{dummy.action.Method.Name}" : value.Renderer.GetType().FullName)}");

    /// <summary>Identifies the current fullscreen shader without querying driver state.</summary>
    internal static string FullscreenName() => ShaderProgramBase.CurrentShaderProgram is { } program
        ? Programs.GetValue(program, static value => $"VS.Fullscreen.{value.AssetDomain ?? "game"}:{value.PassName ?? value.GetType().Name}")
        : "VS.Fullscreen.UnmanagedProgram";
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
