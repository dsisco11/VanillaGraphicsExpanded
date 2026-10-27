using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Builds fixed graphics contracts for binding-only tests from already published executable outputs.</summary>
internal static class ShaderBindingTestPipeline
{
    #region Internal API
    /// <summary>Creates a real pipeline without activation, drawing, native reflection or resource-slot publication.</summary>
    internal static RHIGraphicsPipeline Create(GpuProgram program)
    {
        var graphics = program.Shader.RequireShader().Interface.Graphics
            ?? throw new InvalidOperationException("A ready graphics executable is required.");
        int count = graphics.Outputs.Count == 0 ? 0 : graphics.Outputs.Max(value => value.Location + value.ArraySize);
        var colors = Enumerable.Repeat(new ColorTargetSlot(null, true), count).ToArray();
        foreach (var output in graphics.Outputs)
        {
            TextureFormat format = output.Type switch
            {
                ActiveAttribType.Int or ActiveAttribType.IntVec2 or ActiveAttribType.IntVec3 or ActiveAttribType.IntVec4 => TextureFormat.Rgba32i,
                ActiveAttribType.UnsignedInt or ActiveAttribType.UnsignedIntVec2 or ActiveAttribType.UnsignedIntVec3 or ActiveAttribType.UnsignedIntVec4 => TextureFormat.Rgba32ui,
                _ => TextureFormat.Rgba32f
            };
            for (int element = 0; element < output.ArraySize; element++) colors[output.Location + element] = new(format);
        }
        return RHICommandContext.Current.CreateGraphicsPipeline(new(program.Shader, EngineFullscreenGeometry.Layout,
            new(colors), DynamicPipelineState.Viewport)).RequireResource();
    }
    #endregion
}
