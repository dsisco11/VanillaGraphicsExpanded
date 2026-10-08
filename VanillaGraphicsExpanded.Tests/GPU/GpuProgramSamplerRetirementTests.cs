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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectLightingStopRetiresContractSamplers(bool installHook)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<PBRDirectLightingShaderProgram>();
        using var depth = Texture2D.Create(1, 1, PixelInternalFormat.DepthComponent32f);
        using var input = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var unrelated = GpuSampler.Create();
        unrelated.Bind(9);
        var harmony = new Harmony("VGE.Tests.SamplerRetirement");
        try
        {
            if (installHook) harmony.CreateClassProcessor(typeof(GpuProgramStopHook)).Patch();
            program.PrimaryScene = input.TextureId;
            program.PrimaryDepth = depth.TextureId;
            program.GBufferPosition = input.TextureId;
            using var surfaceInput1 = LayeredTestTexture.Create(input, null, null);
            program.GBufferSurface = surfaceInput1;
            program.ShadowMapNear = depth.TextureId;
            program.ShadowMapFar = depth.TextureId;
            program.Use();
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 4, out int nearSampler);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 5, out int farSampler);
            Assert.NotEqual(0, nearSampler);
            Assert.NotEqual(0, farSampler);
            // Engine callers consume IShaderProgram; a hidden concrete Stop would not protect this boundary.
            ((IShaderProgram)program).Stop();
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 4, out nearSampler);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 5, out farSampler);
            Assert.Equal(installHook, nearSampler == 0);
            Assert.Equal(installHook, farSampler == 0);
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 9, out int retained);
            Assert.Equal(unrelated.SamplerId, retained);
            if (installHook) AssertPlainTextureHandoff();
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            StateCache.Current.UnbindSampler(4);
            StateCache.Current.UnbindSampler(5);
            StateCache.Current.UnbindSampler(9);
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
        StateCache.Current.UseProgram(program.ProgramId);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        ShaderTestFramework.SetUniform(0, 5);
        // Intentionally bypass VGE texture binding: vanilla does not clear foreign sampler objects.
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
        target.BindWithViewport();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        Assert.Equal(new[] { .2f, .4f, .6f, 1f }, target[0].ReadPixels());
        StateCache.Current.InvalidateAll();
    }
    #endregion
}

