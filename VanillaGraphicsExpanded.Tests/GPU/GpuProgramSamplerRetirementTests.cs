using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Protects engine draws from sampler overrides left by explicit VGE contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramSamplerRetirementTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Engine handoff
    /// <summary>The normal interface Stop path retires shadow comparison samplers before engine texture reuse.</summary>
    [Fact]
    public void DirectLightingBoundaryRestoresContractSamplers()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var lights = new VgeLightsUniformBuffer();
        TestUniformRing.EnsureFrame();
        var program = programs.Create<PBRDirectLightingShaderProgram>(owner => { owner.FrameInputs = camera; owner.LightsInputs = lights; });
        using var depth = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.DepthComponent32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var input = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var unrelated = RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource();
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 9, unrelated.SamplerId);
        try
        {
            program.PrimaryScene = input;
            program.PrimaryDepth = depth;
            program.GBufferPosition = input;
            using var surfaceInput1 = LayeredTestTexture.Create(input, null, null);
            program.GBufferSurface = surfaceInput1;
            program.ShadowMapNear = depth;
            program.ShadowMapFar = depth;
            using var pipeline = ShaderBindingTestPipeline.Create(program);
            var context=RHICommandContext.Current;
            using var boundary=context.EnterBoundary(new("Tests.ShadowSampler.Restoration",true,graphics:[pipeline]));
            context.SetPipeline(pipeline);context.BindResources(program.CaptureBindings());
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 4, out int nearSampler);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 5, out int farSampler);
            Assert.NotEqual(0, nearSampler);
            Assert.NotEqual(0, farSampler);
            // The concrete VGE facade withdraws the generated sampler footprint.
            boundary.Dispose();
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 4, out nearSampler);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 5, out farSampler);
            Assert.Equal(0, nearSampler);
            Assert.Equal(0, farSampler);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 9, out int retained);
            Assert.Equal(unrelated.SamplerId, retained);
            AssertPlainTextureHandoff();
        }
        finally
        {
            OpenGLImageBindings.UnbindSampler(OpenGlStateCache.Current, 4);
            OpenGLImageBindings.UnbindSampler(OpenGlStateCache.Current, 5);
            OpenGLImageBindings.UnbindSampler(OpenGlStateCache.Current, 9);
        }
    }

    /// <summary>Emulates an engine draw that relies on texture parameters rather than binding a sampler object.</summary>
    private static void AssertPlainTextureHandoff()
    {
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Load(ShaderType.VertexShader, "tests/complete-state.vsh");
        int fragment = shaders.Load(ShaderType.FragmentShader, "tests/sampler-handoff.fsh");
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var texture = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.2f, .4f, .6f, 1f]);
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
        ShaderTestFramework.SetUniform(0, 5);
        // Intentionally bypass VGE texture binding: vanilla does not clear foreign sampler objects.
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
        FramebufferTestSetup.BindForEngineRendering(target);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        Assert.Equal(new[] { .2f, .4f, .6f, 1f }, target[0].ReadPixels());
        OpenGlStateCache.Current.InvalidateAll();
    }
    #endregion
}

