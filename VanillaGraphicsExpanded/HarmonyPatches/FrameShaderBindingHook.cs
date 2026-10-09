using System;
using System.Collections.Generic;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Restores the universal frame and light publications for engine-owned programs importing VGE shared data.</summary>
internal static class FrameShaderBindingHook
{
    private static readonly Dictionary<int, (bool Frame, bool Lights)> consumers = new();

    #region Public API
    /// <summary>Routes engine program use through the shared snapshots without making ownership effect-specific.</summary>
    internal static void ApplyPatches(Harmony harmony, Action<string> log)
    {
        var use = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use))
            ?? throw new MissingMethodException(typeof(ShaderProgramBase).FullName, nameof(ShaderProgramBase.Use));
        harmony.Patch(use, postfix: new HarmonyMethod(typeof(FrameShaderBindingHook), nameof(UsePostfix)));
        log("[VGE] Patched engine shader use for shared frame and light publications.");
    }

    /// <summary>Withdraws reflected membership when engine program handles are recreated or retired.</summary>
    internal static void ClearUniformCache() => consumers.Clear();
    #endregion

    #region Private
    /// <summary>Binds retained immutable camera and light slices for their consumers on every program use.</summary>
    private static void UsePostfix(ShaderProgramBase __instance)
    {
        int program = __instance.ProgramId;
        if (program == 0) return;
        // Program membership is stable between reloads; global buffer bindings are not.
        if (!consumers.TryGetValue(program, out var membership))
        {
            membership = (GL.GetUniformBlockIndex(program, "VgeFrameUBO") >= 0,
                GL.GetUniformBlockIndex(program, "VgeLightsUBO") >= 0);
            consumers.Add(program, membership);
        }
        if (!membership.Frame && !membership.Lights) return;
        if (__instance.HasUniform("vge_pbrRoute") && !AtmosphereSunDrawHook.Active)
        {
            // GUI and native offscreen routes do not execute the VGE world-shading branches.
            // Use the existing framebuffer/stage contract instead of identifying particular callers.
            var api = VgeFrameRenderer.ActiveApi;
            if (api is null) return;
            var render = api.Render;
            var buffers = render.FrameBuffers;
            int route = PbrDrawRouteHook.Route(render.CurrentRenderStage, render.CurrentFrameBuffer,
                buffers.Count > (int)Vintagestory.API.Client.EnumFrameBuffer.Primary ? buffers[(int)Vintagestory.API.Client.EnumFrameBuffer.Primary] : null,
                buffers.Count > (int)Vintagestory.API.Client.EnumFrameBuffer.Transparent ? buffers[(int)Vintagestory.API.Client.EnumFrameBuffer.Transparent] : null);
            if (route == 0) return;
        }
        if (membership.Frame && !VgeFrameRenderer.Current.TryBindToSlot(GpuBindingRegistry.Ubo.Frame))
            throw new InvalidOperationException("The engine camera consumer could not bind its shared frame snapshot.");
        if (membership.Lights && !VgeLightsRenderer.Current.TryBindToSlot(GpuBindingRegistry.Ubo.Lights))
            throw new InvalidOperationException("The engine light consumer could not bind its shared light snapshot.");
    }
    #endregion
}
