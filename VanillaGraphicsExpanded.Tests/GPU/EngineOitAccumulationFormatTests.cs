using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies the installed transparent allocation boundary and bright layered accumulation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineOitAccumulationFormatTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The installed allocation changes exactly one internal-format operand and retains every other instruction.</summary>
    [Fact]
    public void InstalledAllocationChangesOnlyTheBucketInternalFormat()
    {
        var method = AccessTools.Method(typeof(SystemRenderOITLayers.BeforeOIT), "rebuild");
        var original = PatchProcessor.GetOriginalInstructions(method).ToArray();
        var rewritten = EngineOitAccumulationFormatHook.Transpiler(original).ToArray();
        Assert.Equal(original.Length, rewritten.Length);
        int changes = 0;
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i].opcode, rewritten[i].opcode);
            Assert.Equal(original[i].labels, rewritten[i].labels);
            Assert.Equal(original[i].blocks, rewritten[i].blocks);
            if (Equals(original[i].operand, rewritten[i].operand)) continue;
            changes++;
            Assert.Equal((int)PixelInternalFormat.Rgba16f, original[i].operand);
            Assert.Equal((int)PixelInternalFormat.Rgba32f, rewritten[i].operand);
        }
        Assert.Equal(1, changes);
        Assert.Throws<InvalidOperationException>(() => EngineOitAccumulationFormatHook.Transpiler([]).ToArray());
        Assert.Throws<InvalidOperationException>(() => EngineOitAccumulationFormatHook.Transpiler(original.Concat(original)).ToArray());
    }

    /// <summary>Actual patched engine allocation survives resize, clears all layers and samples finite eight-layer HDR sums.</summary>
    [Fact]
    public void EngineOwnedBucketsResizeClearAndResolveBrightLayers()
    {
        EnsureContextValid();
        var harmony = new Harmony("VGE.Tests.EngineOitAccumulationFormat");
        var type = typeof(SystemRenderOITLayers.BeforeOIT);
        object owner = RuntimeHelpers.GetUninitializedObject(type);
        var render = new Mock<IRenderAPI>();
        var capi = new Mock<ICoreClientAPI>();
        var frames = new List<FrameBufferRef> { new(), new() { Width = 3, Height = 2, FboId = GL.GenFramebuffer() } };
        render.SetupGet(api => api.FrameBuffers).Returns(frames);
        capi.SetupGet(api => api.Render).Returns(render.Object);
        AccessTools.Field(type, "capi").SetValue(owner, capi.Object);
        int vao = GL.GenVertexArray(), draw = 0, sample = 0, targetTexture = 0, targetFbo = 0;
        try
        {
            harmony.CreateClassProcessor(typeof(EngineOitAccumulationFormatHook)).Patch();
            var rebuild = AccessTools.Method(type, "rebuild");
            rebuild.Invoke(owner, null);
            int first = (int)AccessTools.Field(typeof(SystemRenderOITLayers), "accumTextureId").GetValue(null)!;
            AssertStorage(first, 3, 2);
            ClearLayers(frames[1].FboId);
            Assert.All(ReadArray(first, 3, 2), value => Assert.Equal(0, value));
            frames[1].Width = 7;
            frames[1].Height = 3;
            rebuild.Invoke(owner, null);
            int current = (int)AccessTools.Field(typeof(SystemRenderOITLayers), "accumTextureId").GetValue(null)!;
            AssertStorage(current, 7, 3);
            ClearLayers(frames[1].FboId);
            Assert.All(ReadArray(current, 7, 3), value => Assert.Equal(0, value));
            // Use the real bucket attachments and indexed additive factors. Each output uses
            // the installed depth-zero bell weight;
            // Alpha remains unscaled beside HDR RGB.
            draw = CompileProgram("""
                #version 330 core
                layout(location=3) out vec4 bucket0;
                layout(location=4) out vec4 bucket1;
                layout(location=5) out vec4 bucket2;
                void main(){bucket0=vec4(16000,8000,4000,1);bucket1=bucket0*exp(-pow(1.0/.832,2.0));bucket2=bucket0*exp(-pow(2.0/.832,2.0));}
                """);
            GL.BindVertexArray(vao);
            GL.UseProgram(draw);
            GL.Viewport(0, 0, 7, 3);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Enable(EnableCap.Blend);
            GL.ColorMask(true, true, true, true);
            for (int i = 3; i <= 5; i++)
            {
                GL.BlendEquation(i, BlendEquationMode.FuncAdd);
                GL.BlendFunc(i, BlendingFactorSrc.One, BlendingFactorDest.One);
            }
            for (int i = 0; i < 8; i++) GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            var sums = ReadArray(current, 7, 3);
            Assert.Equal(128000, sums[0]);
            Assert.Equal(8, sums[3]);
            Assert.All(sums, value => Assert.True(float.IsFinite(value)));
            sample = CompileProgram("""
                #version 330 core
                uniform sampler2DArray buckets;
                uniform int layer;
                layout(location=0) out vec4 result;
                void main(){vec4 x=texelFetch(buckets,ivec3(0,0,layer),0);result=vec4(x.rgb/x.a,x.a);}
                """);
            targetTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, targetTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            targetFbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, targetTexture, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
            GL.Disable(EnableCap.Blend);
            GL.UseProgram(sample);
            GL.Viewport(0, 0, 1, 1);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2DArray, current);
            GL.Uniform1(GL.GetUniformLocation(sample, "buckets"), 0);
            for (int layer = 0; layer < 3; layer++)
            {
                GL.Uniform1(GL.GetUniformLocation(sample, "layer"), layer);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                float[] result = new float[4];
                GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, result);
                Assert.InRange(result[0], 15999.98f, 16000.02f);
                Assert.InRange(result[1], 7999.99f, 8000.01f);
                Assert.InRange(result[2], 3999.99f, 4000.01f);
                Assert.True(result[3] > 0 && float.IsFinite(result[3]));
            }
            AccessTools.Method(type, "freeResources").Invoke(owner, null);
            Assert.False(GL.IsTexture(current));
            Assert.Equal(0, (int)AccessTools.Field(typeof(SystemRenderOITLayers), "accumTextureId").GetValue(null)!);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            AccessTools.Method(type, "freeResources").Invoke(owner, null);
            harmony.UnpatchAll(harmony.Id);
            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2DArray, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            if (draw != 0) GL.DeleteProgram(draw);
            if (sample != 0) GL.DeleteProgram(sample);
            if (targetTexture != 0) GL.DeleteTexture(targetTexture);
            if (targetFbo != 0) GL.DeleteFramebuffer(targetFbo);
            GL.DeleteVertexArray(vao);
            GL.DeleteFramebuffer(frames[1].FboId);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Checks the actual driver format and extent published by the installed engine owner.</summary>
    private static void AssertStorage(int texture, int width, int height)
    {
        GL.BindTexture(TextureTarget.Texture2DArray, texture);
        GL.GetTexLevelParameter(TextureTarget.Texture2DArray, 0, GetTextureParameter.TextureInternalFormat, out int format);
        GL.GetTexLevelParameter(TextureTarget.Texture2DArray, 0, GetTextureParameter.TextureWidth, out int actualWidth);
        GL.GetTexLevelParameter(TextureTarget.Texture2DArray, 0, GetTextureParameter.TextureHeight, out int actualHeight);
        GL.GetTexLevelParameter(TextureTarget.Texture2DArray, 0, GetTextureParameter.TextureDepth, out int layers);
        Assert.Equal((int)PixelInternalFormat.Rgba32f, format);
        Assert.Equal(width, actualWidth);
        Assert.Equal(height, actualHeight);
        Assert.Equal(3, layers);
    }
    /// <summary>Activates the real six-output mapping and clears its three attached accumulation layers.</summary>
    private static void ClearLayers(int framebuffer)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.DrawBuffers(6, new[] { DrawBuffersEnum.None, DrawBuffersEnum.None, DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment3, DrawBuffersEnum.ColorAttachment4, DrawBuffersEnum.ColorAttachment5 }); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        for (int index = 3; index <= 5; index++) GL.ClearBuffer(ClearBuffer.Color, index, new float[4]);
        Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
    }
    /// <summary>Reads every published bucket texel to check clear and additive payloads independently.</summary>
    private static float[] ReadArray(int texture, int width, int height)
    {
        GL.BindTexture(TextureTarget.Texture2DArray, texture);
        float[] values = new float[width * height * 3 * 4];
        GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.Float, values);
        return values;
    }
    /// <summary>Compiles minimal diagnostic shaders without modifying the production shader catalog.</summary>
    private static int CompileProgram(string fragment)
    {
        string vertex = "#version 330 core\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2-1,0,1);}";
        int program = GL.CreateProgram();
        var shaders = new List<int>();
        try
        {
            foreach (var entry in new[] { (ShaderType.VertexShader, vertex), (ShaderType.FragmentShader, fragment) })
            {
                int shader = GL.CreateShader(entry.Item1);
                shaders.Add(shader);
                GL.ShaderSource(shader, entry.Item2);
                GL.CompileShader(shader);
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
                Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
                GL.AttachShader(program, shader);
            }
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            return program;
        }
        catch
        {
            GL.DeleteProgram(program);
            throw;
        }
        finally
        {
            foreach (int shader in shaders) GL.DeleteShader(shader);
        }
    }
    #endregion
}
