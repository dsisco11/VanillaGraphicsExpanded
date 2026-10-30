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
                ShaderValueType.Int or ShaderValueType.IntVec2 or ShaderValueType.IntVec3 or ShaderValueType.IntVec4 => TextureFormat.Rgba32i,
                ShaderValueType.UnsignedInt or ShaderValueType.UnsignedIntVec2 or ShaderValueType.UnsignedIntVec3 or ShaderValueType.UnsignedIntVec4 => TextureFormat.Rgba32ui,
                _ => TextureFormat.Rgba32f
            };
            for (int element = 0; element < output.ArraySize; element++) colors[output.Location + element] = new(format);
        }
        // Binding-only checks still require a valid complete PSO. Project the already supplied
        // interface, rather than assuming every executable accepts the fullscreen vertex layout.
        var attributes = new System.Collections.Generic.List<RHIVertexAttribute>();
        foreach (var input in graphics.Inputs)
        {
            var shape = ShaderInterfaceShape.From(input.Type);
            var storage = shape.Interpretation switch
            {
                VertexInterpretation.Double => VertexComponentFormat.Double,
                VertexInterpretation.Integer => shape.Unsigned ? VertexComponentFormat.UnsignedInt : VertexComponentFormat.SignedInt,
                _ => VertexComponentFormat.Float
            };
            for (int element = 0; element < input.ArraySize; element++)
            for (int column = 0; column < shape.Columns; column++)
            {
                int location = input.Location + element * shape.Columns + column;
                attributes.Add(new(location, shape.Components, storage, shape.Interpretation,
                    location, 0, shape.Components * (storage == VertexComponentFormat.Double ? 8 : 4)));
            }
        }
        return RHICommandContext.Current.CreateGraphicsPipeline(new(program.Shader, new(attributes),
            new(colors), DynamicPipelineState.Viewport)).RequireResource();
    }
    #endregion
}
