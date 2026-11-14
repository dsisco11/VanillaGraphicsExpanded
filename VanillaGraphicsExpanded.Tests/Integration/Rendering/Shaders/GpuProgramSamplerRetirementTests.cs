using TestUniformRing = VanillaGraphicsExpanded.Tests.Support.GPU.Native.TestUniformRing;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Protects engine draws from sampler overrides left by explicit VGE contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramSamplerRetirementTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    #region Engine handoff
    /// <summary>Disposing the boundary retires the shared shadow-array sampler before engine texture reuse.</summary>
    [Fact]
    public void DirectLightingBoundaryRestoresContractSamplers()
    {
        // Arrange
        EnsureContextValid();
        int[] samplerUnits = [33, 9];
        int[] priorSamplers = new int[samplerUnits.Length];
        for (int index = 0; index < samplerUnits.Length; index++)
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, samplerUnits[index], out priorSamplers[index]);
        try
        {
        using var programs = new ComponentShaderPrograms();
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var lights = new VgeLightsUniformBuffer();
        using var shadowInputs = SurfaceShadowTestInputs.Create(System.Numerics.Vector3.UnitY, System.Numerics.Vector3.Zero);
        TestUniformRing.EnsureFrame();
        var program = programs.Create<PBRDirectLightingShaderProgram>(owner => { owner.FrameInputs = camera; owner.LightsInputs = lights; });
            program.ShadowInputs = shadowInputs;
        using var depth = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.DepthComponent32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var input = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var unrelated = RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource();
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 9, unrelated.SamplerId);
            program.PrimaryScene = input;
            program.PrimaryDepth = depth;
            program.GBufferPosition = input;
            using var surfaceInput1 = LayeredTestTexture.Create(ImageAssertions.Programs, input, null, null);
            program.GBufferSurface = surfaceInput1;
            using var pipeline = ShaderBindingTestPipeline.Create(program);
            var context=RHICommandContext.Current;
            // Emulate the engine's default sampler on the VSM slot while retaining a distinct sampler on an unrelated unit.
            var state = OpenGlStateCache.Current;
            OpenGLImageBindings.BindSampler(state, 33, 0);
            int[] incomingSamplers = new int[samplerUnits.Length];
            for (int index = 0; index < samplerUnits.Length; index++)
                GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, samplerUnits[index], out incomingSamplers[index]);
            Assert.Equal(0, incomingSamplers[0]);
            Assert.Equal(unrelated.SamplerId, incomingSamplers[1]);
            using var boundary=context.EnterBoundary(new("Tests.ShadowSampler.Restoration",true,graphics:[pipeline]));
            // Act
            context.SetPipeline(pipeline);context.BindResources(program.CaptureBindings());
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 33, out int shadowSampler);
            // Assert
            Assert.NotEqual(0, shadowSampler);
            // The concrete VGE facade withdraws the generated sampler footprint.
            boundary.Dispose();
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 33, out shadowSampler);
            Assert.Equal(incomingSamplers[0], shadowSampler);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 9, out int retained);
            Assert.Equal(incomingSamplers[1], retained);
            AssertPlainTextureHandoff();
        }
        finally
        {
            for (int index = 0; index < samplerUnits.Length; index++)
                OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, samplerUnits[index], priorSamplers[index]);
        }
    }


    #endregion
    #endregion
    #region Private
    /// <summary>Emulates an engine draw that relies on texture parameters rather than binding a sampler object.</summary>
    private static void AssertPlainTextureHandoff()
    {
        int priorActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        bool depthEnabled = GL.IsEnabled(EnableCap.DepthTest);
        bool blendEnabled = GL.IsEnabled(EnableCap.Blend);
        bool cullEnabled = GL.IsEnabled(EnableCap.CullFace);
        GL.ActiveTexture(TextureUnit.Texture5);
        int priorTexture = GL.GetInteger(GetPName.TextureBinding2D);
        try
        {
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Load(ShaderType.VertexShader, "tests/complete-state.vsh");
        int fragment = shaders.Load(ShaderType.FragmentShader, "tests/sampler-handoff.fsh");
        using var program = TerrainShaderTestFixture.Link(vertex, fragment);
        using var vao = NativeResourceSetup.CreateVertexArray();
        using var framework = new ShaderTestFramework();
        using var texture = framework.CreateTexture(1, 1, TextureFormat.Rgba32f, [.2f, .4f, .6f, 1f]);
        using var target = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
        ShaderTestFramework.SetNativeUniform(0, 5);
        // Intentionally bypass VGE texture binding: vanilla does not clear foreign sampler objects.
            GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            FramebufferTestSetup.BindForEngineRendering(target);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            Assert.Equal(new[] { .2f, .4f, .6f, 1f }, target[0].ReadPixels());
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, priorTexture);
            GL.ActiveTexture((TextureUnit)priorActiveTexture);
            SetEnabled(EnableCap.DepthTest, depthEnabled);
            SetEnabled(EnableCap.Blend, blendEnabled);
            SetEnabled(EnableCap.CullFace, cullEnabled);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Restores an enable flag to the value observed before the engine handoff draw.</summary>
    private static void SetEnabled(EnableCap capability, bool enabled)
    {
        if (enabled) GL.Enable(capability);
        else GL.Disable(capability);
    }
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
    }
    #endregion
}

