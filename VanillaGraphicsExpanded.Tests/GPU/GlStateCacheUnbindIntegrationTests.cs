using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests verifying GlStateCache Unbind helpers restore a "0" binding for each resource type.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GlStateCacheUnbindIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GlStateCacheUnbindIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void UnbindMethods_SetBindingsToZero()
    {
        fixture.MakeCurrent();
        GlStateCache.Current.InvalidateAll();

        // Program
        using (var program = SimpleProgram.Create("Test.Program"))
        {
            GlStateCache.Current.UseProgram(program.ProgramId);
            Assert.Equal(program.ProgramId, GlStateCache.Current.GetCurrentProgram());

            GlStateCache.Current.UnbindProgram();
            Assert.Equal(0, GlStateCache.Current.GetCurrentProgram());
        }

        // VAO
        int vao = GL.GenVertexArray();
        GlStateCache.Current.BindVertexArray(vao);
        Assert.Equal(vao, GlStateCache.Current.GetCurrentVao());
        GlStateCache.Current.UnbindVertexArray();
        Assert.Equal(0, GlStateCache.Current.GetCurrentVao());
        GL.DeleteVertexArray(vao);

        // FBO
        int fbo = GL.GenFramebuffer();
        GlStateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        Assert.Equal(fbo, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        GlStateCache.Current.UnbindFramebuffer(FramebufferTarget.Framebuffer);
        Assert.Equal(0, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        GL.DeleteFramebuffer(fbo);

        // Program pipeline
        int pipeline = GL.GenProgramPipeline();
        GlStateCache.Current.BindProgramPipeline(pipeline);
        Assert.Equal(pipeline, GlStateCache.Current.GetCurrentProgramPipeline());
        GlStateCache.Current.UnbindProgramPipeline();
        Assert.Equal(0, GlStateCache.Current.GetCurrentProgramPipeline());
        GL.DeleteProgramPipeline(pipeline);

        // Renderbuffer
        int rbo = GL.GenRenderbuffer();
        GlStateCache.Current.BindRenderbuffer(rbo);
        Assert.Equal(rbo, GlStateCache.Current.GetCurrentRenderbuffer());
        GlStateCache.Current.UnbindRenderbuffer();
        Assert.Equal(0, GlStateCache.Current.GetCurrentRenderbuffer());
        GL.DeleteRenderbuffer(rbo);

        // Transform feedback
        int tf = GL.GenTransformFeedback();
        GlStateCache.Current.BindTransformFeedback(tf);
        Assert.Equal(tf, GlStateCache.Current.GetCurrentTransformFeedback());
        GlStateCache.Current.UnbindTransformFeedback();
        Assert.Equal(0, GlStateCache.Current.GetCurrentTransformFeedback());
        GL.DeleteTransformFeedback(tf);

        // Texture + sampler
        int tex = GL.GenTexture();
        const int unit = 5;
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit, tex);
        Assert.Equal(tex, GlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, unit));
        GlStateCache.Current.UnbindTexture(TextureTarget.Texture2D, unit);
        Assert.Equal(0, GlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, unit));
        GL.DeleteTexture(tex);

        int sampler = GL.GenSampler();
        GlStateCache.Current.BindSampler(unit, sampler);
        Assert.Equal(sampler, GlStateCache.Current.GetBoundSampler(unit));
        GlStateCache.Current.UnbindSampler(unit);
        Assert.Equal(0, GlStateCache.Current.GetBoundSampler(unit));
        GL.DeleteSampler(sampler);

        // Buffer
        int buffer = GL.GenBuffer();
        GlStateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, buffer);
        Assert.Equal(buffer, GlStateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
        GlStateCache.Current.UnbindBuffer(BufferTarget.ArrayBuffer);
        Assert.Equal(0, GlStateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
        GL.DeleteBuffer(buffer);
    }

    private sealed class SimpleProgram : IDisposable
    {
        public int ProgramId { get; }

        private readonly int vs;
        private readonly int fs;

        private SimpleProgram(int programId, int vs, int fs)
        {
            ProgramId = programId;
            this.vs = vs;
            this.fs = fs;
        }

        public static SimpleProgram Create(string? debugName)
        {
            const string vsSource = """
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

            const string fsSource = """
                #version 430 core
                out vec4 color;
                void main()
                {
                    color = vec4(1.0, 0.0, 1.0, 1.0);
                }
                """;

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

            return new SimpleProgram(program, vs, fs);
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

