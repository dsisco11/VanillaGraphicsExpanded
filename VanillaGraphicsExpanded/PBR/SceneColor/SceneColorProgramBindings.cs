using System;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Assigns color convention at scene bindings and explicit engine postprocess submissions.</summary>
internal static class SceneColorProgramBindings
{
    #region Public API
    /// <summary>Resets reused engine executables on every binding, including UI and offscreen calls.</summary>
    internal static void BindScene(ShaderProgramBase program, ICoreClientAPI? api)
    {
        if (program is GpuProgram || !program.HasUniform("vge_sceneLinear")) return;
        bool linear = false;
        if (api is not null)
        {
            var render = api.Render;
            var buffers = render.FrameBuffers;
            linear = SelectScene(render.CurrentRenderStage, render.CurrentFrameBuffer,
                buffers.Count > (int)EnumFrameBuffer.Primary ? buffers[(int)EnumFrameBuffer.Primary] : null,
                buffers.Count > (int)EnumFrameBuffer.Transparent ? buffers[(int)EnumFrameBuffer.Transparent] : null);
            // These programs interpret sampled inputs, not their destination. Their actual
            // engine call sites select HDR after Use; a generic or nested use remains legacy.
            if (program.PassName is "final" or "colorgrade" or "luma" or "godrays") linear = false;
        }
        if (linear) RequireConvention(program);
        program.Uniform("vge_sceneLinear", linear ? 1 : 0);
    }

    /// <summary>Requires both a scene stage and its authoritative target; offscreen callbacks stay display-referred.</summary>
    internal static bool SelectScene(EnumRenderStage stage, FrameBufferRef? current,
        FrameBufferRef? primary, FrameBufferRef? transparent)
    {
        if (current is null) return false;
        var expected = stage switch
        {
            EnumRenderStage.Opaque or EnumRenderStage.AfterOIT => primary,
            EnumRenderStage.OIT => transparent,
            _ => null
        };
        return expected is not null && current.FboId == expected.FboId;
    }

    /// <summary>Preserves engine activation, then supplies the known scene-input convention at its owning call site.</summary>
    internal static void UsePostprocess(ShaderProgramBase program)
    {
        program.Use();
        bool sceneInput = SceneColorPipeline.HasSceneInput
            && program.PassName is "final" or "luma" or "godrays";
        if (sceneInput) RequireConvention(program);
        if (program.HasUniform("vge_sceneLinear"))
            program.Uniform("vge_sceneLinear", sceneInput ? 1 : 0);
    }
    #endregion

    #region Private
    /// <summary>Reports a broken owned shader contract instead of silently emitting or consuming display RGB.</summary>
    private static void RequireConvention(ShaderProgramBase program)
    {
        if (!ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention)
            || !program.HasUniform("vge_sceneLinear"))
            throw new InvalidOperationException($"VGE HDR color binding is unavailable for {program.PassName}.");
    }
    #endregion
}
