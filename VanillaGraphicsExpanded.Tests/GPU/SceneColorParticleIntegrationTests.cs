using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks installed particle hook compatibility and real redirect boundary restoration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorParticleIntegrationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The hook brackets the original model-selecting submission and cannot skip or swallow it.</summary>
    [Fact]
    public void InstalledHookTargetsOriginalSubmissionWithoutReplacement()
    {
        var target = SceneColorParticleCaptureHook.TargetMethod();
        Assert.Equal("Vintagestory.Client.NoObf.SystemRenderParticles", target.DeclaringType!.FullName);
        Assert.Equal("Render", target.Name);
        Assert.Equal(new[] { typeof(int), typeof(float) }, target.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(void), ((MethodInfo)target).ReturnType);
        Assert.Equal(typeof(void), AccessTools.Method(typeof(SceneColorParticleCaptureHook), nameof(SceneColorParticleCaptureHook.Prefix)).ReturnType);
        Assert.Equal(typeof(void), AccessTools.Method(typeof(SceneColorParticleCaptureHook), nameof(SceneColorParticleCaptureHook.Finalizer)).ReturnType);
        var dispatcher = AccessTools.Method(target.DeclaringType, "OnRenderFrame3D");
        Assert.Contains(PatchProcessor.GetOriginalInstructions(dispatcher), instruction => instruction.Calls((MethodInfo)target));
        var harmony = new Harmony("VGE.Tests.SceneParticleCaptureHook");
        try
        {
            harmony.CreateClassProcessor(typeof(SceneColorParticleCaptureHook)).Patch();
            var patches = Harmony.GetPatchInfo(target)!;
            Assert.Contains(patches.Prefixes, patch => patch.owner == harmony.Id);
            Assert.Contains(patches.Finalizers, patch => patch.owner == harmony.Id);
            Assert.DoesNotContain(patches.Transpilers, patch => patch.owner == harmony.Id);
            SceneColorParticleCaptureHook.Prefix(1, out var inactive);
            Assert.Null(inactive);
            SceneColorParticleCaptureHook.Finalizer(new InvalidOperationException("Original failure"), inactive);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Success publishes only after the engine draw; exceptions restore routing and withdraw partial capture.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawScopeRestoresEngineBlendAndIndependentBindings(bool failed)
    {
        EnsureContextValid();
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba32f);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent32f);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = GpuFramebuffer.Create([material, glow], depth);
        using var other = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var capture = new SceneColorParticleTargets(primary);
        primary.BindWithViewport();
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        GL.ColorMask(true, true, true, true);
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { .125f, .25f, .5f, 1f });
        GL.ClearBuffer(ClearBuffer.Color, 1, new[] { .2f, .3f, .4f, .5f });
        GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { .75f });
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, other.FboId);
        GL.Enable(IndexedEnableCap.Blend, 1);
        GL.BlendFuncSeparate(1, BlendingFactorSrc.Zero, BlendingFactorDest.One, BlendingFactorSrc.Zero, BlendingFactorDest.One);
        GL.ColorMask(2, false, true, false, true);
        StateCache.Current.InvalidateAll();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            void main(){vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));gl_Position=vec4(p[gl_VertexID],0,1);}
            """);
        int fragment = shaders.Compile(ShaderType.FragmentShader, """
            #version 430 core
            layout(location=0) out vec4 color;layout(location=1) out vec4 glow;
            void main(){color=vec4(8,4,2,.5);glow=vec4(1);gl_FragDepth=.5;}
            """);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        try
        {
            var scope = new SceneColorParticleDrawScope(capture);
            Assert.Equal(capture.DrawTarget.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            GL.UseProgram(program.ProgramId);
            GL.BindVertexArray(vao.VertexArrayId);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            SceneColorParticleCaptureHook.Finalizer(failed ? new InvalidOperationException("Original draw failed") : null, scope);
            scope.Dispose();
            Assert.Equal(!failed, capture.Captured);
            Assert.Equal(primary.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(other.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 0));
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 1));
            Assert.Equal((int)BlendingFactorSrc.SrcAlpha, Indexed(GetPName.BlendSrcRgb, 0));
            Assert.Equal((int)BlendingFactorSrc.SrcAlpha, Indexed(GetPName.BlendSrcAlpha, 0));
            Assert.Equal((int)BlendingFactorDest.OneMinusSrcAlpha, Indexed(GetPName.BlendDstRgb, 0));
            Assert.Equal((int)BlendingFactorSrc.Zero, Indexed(GetPName.BlendSrcRgb, 1));
            Assert.Equal((int)BlendingFactorDest.One, Indexed(GetPName.BlendDstRgb, 1));
            bool[] metadataMask = new bool[4];
            GL.GetBoolean(GetIndexedPName.ColorWritemask, 2, metadataMask);
            Assert.Equal(new[] { false, true, false, true }, metadataMask);
            Assert.Equal(new[] { 4f, 2f, 1f, .5f }, capture.DrawTarget[0].ReadPixels()[..4]);
            Assert.Equal(new[] { .125f, .25f, .5f, 1f }, primary[0].ReadPixels()[..4]);
            Assert.Equal(new[] { .2f, .3f, .4f, .5f }, primary[1].ReadPixels()[..4]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.Disable(EnableCap.Blend);
            GL.ColorMask(true, true, true, true);
            GL.UseProgram(0);
            GL.BindVertexArray(0);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Observes one indexed blend factor after the production restoration boundary.</summary>
    private static int Indexed(GetPName name, int index)
    {
        GL.GetInteger((GetIndexedPName)name, index, out int result);
        return result;
    }
    #endregion
}
