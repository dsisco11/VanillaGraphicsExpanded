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
        StateCache.Current.InvalidateAll();

        // Program
        using (var program = SimpleProgram.Create("Test.Program"))
        {
            StateCache.Current.UseProgram(program.ProgramId);
            Assert.Equal(program.ProgramId, StateCache.Current.GetCurrentProgram());

            StateCache.Current.UnbindProgram();
            Assert.Equal(0, StateCache.Current.GetCurrentProgram());
        }

        // VAO
        int vao = GL.GenVertexArray();
        StateCache.Current.BindVertexArray(vao);
        Assert.Equal(vao, StateCache.Current.GetCurrentVao());
        StateCache.Current.UnbindVertexArray();
        Assert.Equal(0, StateCache.Current.GetCurrentVao());
        GL.DeleteVertexArray(vao);

        // FBO
        int fbo = GL.GenFramebuffer();
        StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        Assert.Equal(fbo, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        StateCache.Current.UnbindFramebuffer(FramebufferTarget.Framebuffer);
        Assert.Equal(0, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        GL.DeleteFramebuffer(fbo);

        // Program pipeline
        int pipeline = GL.GenProgramPipeline();
        StateCache.Current.BindProgramPipeline(pipeline);
        Assert.Equal(pipeline, StateCache.Current.GetCurrentProgramPipeline());
        StateCache.Current.UnbindProgramPipeline();
        Assert.Equal(0, StateCache.Current.GetCurrentProgramPipeline());
        GL.DeleteProgramPipeline(pipeline);

        // Renderbuffer
        int rbo = GL.GenRenderbuffer();
        StateCache.Current.BindRenderbuffer(rbo);
        Assert.Equal(rbo, StateCache.Current.GetCurrentRenderbuffer());
        StateCache.Current.UnbindRenderbuffer();
        Assert.Equal(0, StateCache.Current.GetCurrentRenderbuffer());
        GL.DeleteRenderbuffer(rbo);

        // Transform feedback
        int tf = GL.GenTransformFeedback();
        StateCache.Current.BindTransformFeedback(tf);
        Assert.Equal(tf, StateCache.Current.GetCurrentTransformFeedback());
        StateCache.Current.UnbindTransformFeedback();
        Assert.Equal(0, StateCache.Current.GetCurrentTransformFeedback());
        GL.DeleteTransformFeedback(tf);

        // Texture + sampler
        int tex = GL.GenTexture();
        const int unit = 5;
        StateCache.Current.BindTexture(TextureTarget.Texture2D, unit, tex);
        Assert.Equal(tex, StateCache.Current.GetBoundTexture(TextureTarget.Texture2D, unit));
        StateCache.Current.UnbindTexture(TextureTarget.Texture2D, unit);
        Assert.Equal(0, StateCache.Current.GetBoundTexture(TextureTarget.Texture2D, unit));
        GL.DeleteTexture(tex);

        int sampler = GL.GenSampler();
        StateCache.Current.BindSampler(unit, sampler);
        Assert.Equal(sampler, StateCache.Current.GetBoundSampler(unit));
        StateCache.Current.UnbindSampler(unit);
        Assert.Equal(0, StateCache.Current.GetBoundSampler(unit));
        GL.DeleteSampler(sampler);

        // Buffer
        int buffer = GL.GenBuffer();
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, buffer);
        Assert.Equal(buffer, StateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
        StateCache.Current.UnbindBuffer(BufferTarget.ArrayBuffer);
        Assert.Equal(0, StateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
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
            const string vsSource = "tests/GlStateCacheUnbindIntegrationTests_1.vsh";

            const string fsSource = "tests/GlStateCacheUnbindIntegrationTests_2.fsh";

            int vs = Compile(ShaderType.VertexShader, vsSource);
            int fs = Compile(ShaderType.FragmentShader, fsSource);

            int program = GL.CreateProgram();
            if (program == 0)
            {
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(vs);
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(fs);
                throw new InvalidOperationException("glCreateProgram returned 0.");
            }

#if DEBUG
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, program, debugName);
#endif

            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkStatus);
            string info = GL.GetProgramInfoLog(program) ?? string.Empty;
            if (linkStatus == 0)
            {
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(program);
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(vs);
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(fs);
                throw new InvalidOperationException($"Program link failed: {info}");
            }

            return new SimpleProgram(program, vs, fs);
        }

        private static int Compile(ShaderType type, string source)
        {
            int id = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.Load(source, type);
            GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
            string info = GL.GetShaderInfoLog(id) ?? string.Empty;
            if (status == 0)
            {
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(id);
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
                    global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(ProgramId);
                }
            }
            catch
            {
            }

            try { if (vs != 0) global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(vs); } catch { }
            try { if (fs != 0) global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(fs); } catch { }
        }
    }
}
