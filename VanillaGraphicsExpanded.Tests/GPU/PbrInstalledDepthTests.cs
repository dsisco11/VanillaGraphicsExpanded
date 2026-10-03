using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks coverage and depth using the complete installed standard shader pair.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrInstalledDepthTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Installed terrain-independent meshes
    /// <summary>Opaque container and first-person draws retain alpha and reject a later background draw.</summary>
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 0)]
    [InlineData(1, 1, 2, 1)]
    public void StandardMeshOccludesBackground(int offsetVariant, int route, int ssao, int oit = 0)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build("standard.vsh", 0, oit, ssao, 0, offsetVariant));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("standard.fsh", 0, oit, ssao, 0, offsetVariant));
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var vertices = GpuVbo.Create();
        using var framework = new ShaderTestFramework();
        using var texture = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [1f, .25f, .1f, 1f]);
        using var color = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var glow = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var engineNormal = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var position = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var gbuffer = new GBufferTextures(1, 1);
        using var target = GpuFramebuffer.CreateMRT([color, glow, engineNormal, position,
            gbuffer.Normal, gbuffer.Material, gbuffer.PatchId, gbuffer.Environment], depth)!;
        var layout = GpuProgramLayout.TryBuild(program.ProgramId);
        GlStateCache.Current.UseProgram(program.ProgramId);
        GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        vertices.UploadData<float>([-1, -1, -.5f, 3, -1, -.5f, -1, 3, -.5f]);
        vertices.Bind();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
        GL.VertexAttrib2(1, .5f, .5f); GL.VertexAttrib4(2, 1f, 1f, 1f, 1f);
        // Engine vertex flags encode positive Y magnitude in bits 18..20.
        GL.VertexAttribI1(3, 7 << 18);
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        foreach (string name in new[] { "modelMatrix", "viewMatrix", "projectionMatrix" })
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program.ProgramId, name), identity);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "rgbaTint"), 1f, 1f, 1f, 1f);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "rgbaLightIn"), 1f, 1f, 1f, 1f);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "rgbaAmbientIn"), 1f, 1f, 1f);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "viewDistance"), 256f);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "dontWarpVertices"), 1);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_pbrRoute"), route);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereAerialRadiance"), 11);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereAerialAttenuation"), 12);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "depthOffset"), -.05f);
        texture.Bind(0);
        target.BindWithViewport();
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
        GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        GL.ClearColor(0, 0, 0, 0); GL.ClearDepth(1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        float[] foreground = target[0].ReadPixels();
        Assert.Equal(1f, foreground[3]);
        if (route == 1)
        {
            // Deferred capture must publish unlit albedo and overwrite the cleared material target.
            Assert.InRange(foreground[0], .999f, 1.001f);
            Assert.InRange(gbuffer.Material.ReadPixels()[0], .499f, .501f);
            float[] normal = gbuffer.Normal.ReadPixels();
            Assert.Equal(.5f, normal[0]);
            Assert.Equal(1f, normal[1]);
            Assert.Equal(.5f, normal[2]);
        }
        if (offsetVariant > 0 && route == 1)
        {
            Assert.Equal(-1f, gbuffer.Normal.ReadPixels()[3]);
            Assert.InRange(position.ReadPixels()[2], -.501f, -.499f);
        }
        // Draw a distinguishable farther opaque mesh after the first mesh. Depth must reject it.
        vertices.UploadData<float>([-1, -1, .5f, 3, -1, .5f, -1, 3, .5f]);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "rgbaTint"), 0f, 1f, 0f, .25f);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        Assert.Equal(foreground, target[0].ReadPixels());
    }
    #endregion
}
