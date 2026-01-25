using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests verifying per-attachment (indexed) blend state behavior for MRT framebuffers.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuFramebufferBlendStateIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuFramebufferBlendStateIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void AttachmentBlendEnable_OverridesGlobalBlendDisable()
    {
        fixture.MakeCurrent();
        GlStateCache.Current.InvalidateAll();

        using var t0 = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8, debugName: "Test.Att0");
        using var t1 = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8, debugName: "Test.Att1");
        using var fbo = GpuFramebuffer.CreateMRT([t0, t1], debugName: "Test.Fbo")!;

        fbo.BindWithViewport();

        // Global blending disabled...
        GlStateCache.Current.Apply(new GlPipelineDesc(
            defaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnable),
            nonDefaultMask: new GlPipelineStateMask(0),
            validate: false));

        ClearColorAttachments(fbo.FboId, drawBufferCount: 2, r: 0, g: 0, b: 0, a: 0);
        UseMrtDrawBuffers(2);

        // ...but attachment 0 blending explicitly enabled.
        fbo.SetAttachmentBlendState(
            attachmentIndex: 0,
            enabled: true,
            srcRgb: BlendingFactorSrc.SrcAlpha,
            dstRgb: BlendingFactorDest.OneMinusSrcAlpha,
            srcAlpha: BlendingFactorSrc.SrcAlpha,
            dstAlpha: BlendingFactorDest.OneMinusSrcAlpha);
        fbo.SetAttachmentBlendEnabled(1, enabled: false);
        fbo.ApplyAttachmentBlendState();

        using var program = SimpleMrtProgram.Create(
            fragmentColor: "vec4(1.0, 0.0, 0.0, 0.5)",
            debugName: "Test.Program");

        int vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);

        GL.UseProgram(program.ProgramId);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.Finish();

        var c0 = ReadRgba8(fbo.FboId, attachmentIndex: 0);
        var c1 = ReadRgba8(fbo.FboId, attachmentIndex: 1);

        // Attachment 0 should have blended: 0.5 red over black => ~128.
        Assert.InRange(c0.R, 110, 150);
        // Attachment 1 should not have blended (global blend disabled): full red => 255.
        Assert.InRange(c1.R, 245, 255);

        GL.BindVertexArray(0);
        GL.DeleteVertexArray(vao);
    }

    [Fact]
    public void AttachmentBlendDisable_OverridesGlobalBlendEnable()
    {
        fixture.MakeCurrent();
        GlStateCache.Current.InvalidateAll();

        using var t0 = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8, debugName: "Test.Att0");
        using var t1 = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8, debugName: "Test.Att1");
        using var fbo = GpuFramebuffer.CreateMRT([t0, t1], debugName: "Test.Fbo")!;

        fbo.BindWithViewport();

        // Global blending enabled (alpha blend).
        GlStateCache.Current.Apply(new GlPipelineDesc(
            defaultMask: new GlPipelineStateMask(0),
            nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.BlendFunc),
            blendFunc: new GlBlendFunc(
                BlendingFactorSrc.SrcAlpha,
                BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.SrcAlpha,
                BlendingFactorDest.OneMinusSrcAlpha),
            validate: false));

        // Clear to blue so blended result differs from overwrite.
        ClearColorAttachments(fbo.FboId, drawBufferCount: 2, r: 0, g: 0, b: 255, a: 255);
        UseMrtDrawBuffers(2);

        // Explicitly disable blending for attachment 1.
        fbo.SetAttachmentBlendEnabled(1, enabled: false);
        fbo.ApplyAttachmentBlendState();

        using var program = SimpleMrtProgram.Create(
            fragmentColor: "vec4(1.0, 0.0, 0.0, 0.5)",
            debugName: "Test.Program");

        int vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);

        GL.UseProgram(program.ProgramId);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.Finish();

        var c1 = ReadRgba8(fbo.FboId, attachmentIndex: 1);

        // If global blend leaked through, we'd get ~128 red + ~128 blue.
        // With indexed disable, we expect a full overwrite to red.
        Assert.InRange(c1.R, 245, 255);
        Assert.InRange(c1.B, 0, 10);

        GL.BindVertexArray(0);
        GL.DeleteVertexArray(vao);
    }

    private static void UseMrtDrawBuffers(int colorAttachmentCount)
    {
        var bufs = new DrawBuffersEnum[colorAttachmentCount];
        for (int i = 0; i < colorAttachmentCount; i++)
        {
            bufs[i] = DrawBuffersEnum.ColorAttachment0 + i;
        }

        GL.DrawBuffers(colorAttachmentCount, bufs);
    }

    private static void ClearColorAttachments(int fboId, int drawBufferCount, byte r, byte g, byte b, byte a)
    {
        GlStateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;
        float af = a / 255f;

        float[] color = [rf, gf, bf, af];
        for (int i = 0; i < drawBufferCount; i++)
        {
            GL.ClearBuffer(ClearBuffer.Color, i, color);
        }
    }

    private static (byte R, byte G, byte B, byte A) ReadRgba8(int fboId, int attachmentIndex)
    {
        GlStateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fboId);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + attachmentIndex);

        byte[] px = new byte[4];
        GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, px);
        return (px[0], px[1], px[2], px[3]);
    }

    private sealed class SimpleMrtProgram : IDisposable
    {
        public int ProgramId { get; }

        private readonly int vs;
        private readonly int fs;

        private SimpleMrtProgram(int programId, int vs, int fs)
        {
            ProgramId = programId;
            this.vs = vs;
            this.fs = fs;
        }

        public static SimpleMrtProgram Create(string fragmentColor, string? debugName = null)
        {
            string vsSource = """
                #version 430 core
                void main()
                {
                    vec2 pos;
                    if (gl_VertexID == 0) pos = vec2(-1.0, -1.0);
                    else if (gl_VertexID == 1) pos = vec2( 3.0, -1.0);
                    else pos = vec2(-1.0,  3.0);
                    gl_Position = vec4(pos, 0.0, 1.0);
                }
                """;

            string fsSource = """
                #version 430 core
                layout(location=0) out vec4 o0;
                layout(location=1) out vec4 o1;
                void main()
                {
                    vec4 c = FRAGMENT_COLOR;
                    o0 = c;
                    o1 = c;
                }
                """.Replace("FRAGMENT_COLOR", fragmentColor, StringComparison.Ordinal);

            int vs = Compile(ShaderType.VertexShader, vsSource);
            int fs = Compile(ShaderType.FragmentShader, fsSource);

            int program = GL.CreateProgram();
            if (program == 0)
            {
                GL.DeleteShader(vs);
                GL.DeleteShader(fs);
                throw new InvalidOperationException("glCreateProgram returned 0.");
            }

#if DEBUG
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, program, debugName);
#endif

            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkStatus);
            string info = GL.GetProgramInfoLog(program) ?? string.Empty;
            if (linkStatus == 0)
            {
                GL.DeleteProgram(program);
                GL.DeleteShader(vs);
                GL.DeleteShader(fs);
                throw new InvalidOperationException($"Program link failed: {info}");
            }

            return new SimpleMrtProgram(program, vs, fs);
        }

        private static int Compile(ShaderType type, string source)
        {
            int id = GL.CreateShader(type);
            if (id == 0)
            {
                throw new InvalidOperationException("glCreateShader returned 0.");
            }

            GL.ShaderSource(id, source);
            GL.CompileShader(id);

            GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
            string info = GL.GetShaderInfoLog(id) ?? string.Empty;
            if (status == 0)
            {
                GL.DeleteShader(id);
                throw new InvalidOperationException($"{type} compile failed: {info}");
            }

            return id;
        }

        public void Dispose()
        {
            try
            {
                if (ProgramId != 0)
                {
                    GL.UseProgram(0);
                    GL.DetachShader(ProgramId, vs);
                    GL.DetachShader(ProgramId, fs);
                    GL.DeleteProgram(ProgramId);
                }
            }
            catch
            {
            }

            try { if (vs != 0) GL.DeleteShader(vs); } catch { }
            try { if (fs != 0) GL.DeleteShader(fs); } catch { }
        }
    }
}
