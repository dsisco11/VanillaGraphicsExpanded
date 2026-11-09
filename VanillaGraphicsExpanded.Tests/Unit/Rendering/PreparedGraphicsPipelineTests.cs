using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks prepared graphics ownership against real compiled production executables.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PreparedGraphicsPipelineTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    #region Public API





    /// <summary>Linked tessellation output refines conservative patch relevance without descriptor queries.</summary>
    [Fact]
    public void VerifiedTessellationOutputRefinesPrimitiveRelevanceWithoutQueries()
    {
        // Arrange: Compile a tessellation pipeline whose evaluation stage declares triangle output.
        fixture.MakeCurrent();
        var shaderDescription = new RHIShaderDescription(ShaderKind.Graphics,
            [new(ShaderStage.Vertex, "#version 450\nvoid main() { gl_Position=vec4(0,0,0,1); }"),
             new(ShaderStage.TessellationControl, "#version 450\nlayout(vertices=3) out; void main() { gl_out[gl_InvocationID].gl_Position=gl_in[gl_InvocationID].gl_Position; if(gl_InvocationID==0) { gl_TessLevelInner[0]=1; gl_TessLevelOuter[0]=1; gl_TessLevelOuter[1]=1; gl_TessLevelOuter[2]=1; } }"),
             new(ShaderStage.TessellationEvaluation, "#version 450\nlayout(triangles,equal_spacing,ccw) in; void main() { gl_Position=gl_TessCoord.x*gl_in[0].gl_Position+gl_TessCoord.y*gl_in[1].gl_Position+gl_TessCoord.z*gl_in[2].gl_Position; }"),
             new(ShaderStage.Fragment, "#version 450\nlayout(location=0) out vec4 color; void main() { color=vec4(1); }")], new());
        using var reference = RHICommandContext.Current.CreateShader(shaderDescription).RequireResource();
        var metadata = reference.Interface.Graphics!;
        Assert.Equal(PrimitiveTopology.Triangles, metadata.TessellationOutput);
        var description = new RHIGraphicsPipelineDescription(reference, new([]),
            new([new(TextureFormat.Rgba16f)]), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveTopology.Patches });
        var conservative = GraphicsStateRelevance.Derive(description, GpuSupport.Graphics);
        // Act: Derive relevance without linked output facts, then with the verified executable metadata.
        var prepared = GraphicsStateRelevance.Derive(description, GpuSupport.Graphics, metadata);
        // Assert: Verified output removes unnecessary line/point knowledge while retaining unrelated rasterizer state.
        Assert.True(conservative.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize));
        Assert.False(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth));
        Assert.False(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(prepared.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.FrontFace));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the production fullscreen shader's explicit vertex and floating color interface.</summary>
    private static RHIGraphicsPipelineDescription Description(GpuProgram shader, int components = 3, TextureFormat format = TextureFormat.Rgba16f) =>
        new(shader.Shader, new([new(0, components, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]),
            new(shader is VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram
                ? [new(format), new(format), new(TextureFormat.R32f)] : [new(format)]), DynamicPipelineState.Viewport);
    #endregion
}

