using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes complete installed terrain stages to verify nonlinear field conversion after raster interpolation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrBlockFieldRasterTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A real native triangle publishes the response of its interpolated raw field, rather than interpolated endpoint illumination.</summary>
    [Theory]
    [InlineData("chunkopaque")]
    [InlineData("chunktopsoil")]
    public void InstalledTerrainEvaluatesBlockFieldPerPixel(string family)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build(family + ".vsh", 0, 0, 0, 0, 0));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build(family + ".fsh", 0, 0, 0, 0, 0));
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = NativeResourceSetup.CreateVertexArray();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(8, 8, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32ui, PixelInternalFormat.Rgba32f);
        using var texture = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [1f, 1f, 1f, 1f]);
        using var camera = TestFrameCamera.CreateIdentity(8, 8);
        camera.SetLightingCalibration(LightingCalibration.Default);
        Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Frame, camera.Capture()));
        int buffer = GL.GenBuffer();
        try
        {
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            // The lower edge spans neutral floor to torch. All three clip-space W values are one,
            // so interior samples have a known affine raw field without a hand-authored fragment varying.
            float floor = 10f / 255;
            float[] vertices = [-1,-1,0, floor,floor,floor,0,
                1,-1,0, 145f/255,99f/255,71f/255,0,
                -1,1,0, floor,floor,floor,0];
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
            GL.VertexAttrib2(1, .5f, .5f); GL.VertexAttribI1(3, 0);
            if (family == "chunktopsoil") { GL.VertexAttrib2(4, .5f, .5f); GL.VertexAttribI1(5, 0); }
            else GL.VertexAttribI1(4, 0);
            float[] identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
            foreach (string matrix in new[] { "projectionMatrix", "modelViewMatrix" })
            {
                int location = GL.GetUniformLocation(program.ProgramId, matrix);
                if (location >= 0) GL.UniformMatrix4(location, 1, false, identity);
            }
            GL.Uniform1(GL.GetUniformLocation(program.ProgramId, "viewDistance"), 100f);
            GL.Uniform1(GL.GetUniformLocation(program.ProgramId, "viewDistanceLod0"), 100f);
            // Separate sampler types even when disabled branches do not fetch their textures.
            GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveUniforms, out int uniformCount);
            for (int index = 0; index < uniformCount; index++)
            {
                string name = GL.GetActiveUniform(program.ProgramId, index, out _, out ActiveUniformType type);
                int unit = type switch { ActiveUniformType.Sampler2D => 0, ActiveUniformType.Sampler2DArray => 1,
                    ActiveUniformType.UnsignedIntSampler2DArray => 2, ActiveUniformType.UnsignedIntSampler2D => 3, _ => -1 };
                if (unit >= 0) GL.Uniform1(GL.GetUniformLocation(program.ProgramId, name), unit);
            }
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texture, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)texture).Target))]);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest); GL.ColorMask(true, true, true, true);
            FramebufferTestSetup.BindForEngineRendering(target); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            float[] pixels = target[7].ReadPixels();
            for (int column = 0; column < 7; column++)
            {
                double t = (column + .5) / 8;
                double[] raw = [10.0 / 255 + t * 135 / 255, 10.0 / 255 + t * 89 / 255, 10.0 / 255 + t * 61 / 255];
                double peak = raw[0], amplitude = peak - 10.0 / 255;
                double gain = LightingCalibration.Default.BlockReferenceLux * Math.Pow(amplitude, 4)
                    / (Math.Pow(amplitude, 3) + .064) / peak / Math.PI;
                for (int channel = 0; channel < 3; channel++)
                    Assert.InRange(pixels[column * 4 + channel], raw[channel] * gain - .00001, raw[channel] * gain + .00001);
                Assert.Equal(0, pixels[column * 4 + 3]);
            }
            // The first two interior samples would be much brighter if only calibrated endpoints were interpolated.
            double torchRed = LightingCalibration.Default.BlockReferenceLux * Math.Pow(135.0 / 255, 4)
                / (Math.Pow(135.0 / 255, 3) + .064) / Math.PI;
            Assert.True(pixels[4] < torchRed * (1.5 / 8) * .1);
        }
        finally
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.DeleteBuffer(buffer);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
