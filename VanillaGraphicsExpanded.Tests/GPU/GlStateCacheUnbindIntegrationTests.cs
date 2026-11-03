using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests verifying GlStateCache Unbind helpers restore a "0" binding for each resource type.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GlStateCacheUnbindIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Creates integration tests using the shared headless graphics fixture.</summary>
    /// <param name="fixture">The fixture that owns the OpenGL context.</param>
    public GlStateCacheUnbindIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Verifies each resource selection can be returned to its unbound state.</summary>
    [Fact]
    public void UnbindMethods_SetBindingsToZero()
    {
        fixture.MakeCurrent();
        OpenGlStateCache.Current.InvalidateAll();

        // Program
        using (var program = SimpleProgram.Create("Test.Program"))
        {
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.Program.ProgramId);
            Assert.Equal(program.Program.ProgramId, OpenGlStateCache.Current.GetCurrentProgram());

            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            Assert.Equal(0, OpenGlStateCache.Current.GetCurrentProgram());
        }

        // VAO
        int vao = GL.GenVertexArray();
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao);
        Assert.Equal(vao, OpenGlStateCache.Current.GetCurrentVao());
        OpenGlStateCache.Current.UnbindVertexArray();
        Assert.Equal(0, OpenGlStateCache.Current.GetCurrentVao());
        GL.DeleteVertexArray(vao);

        // FBO
        int fbo = GL.GenFramebuffer();
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, fbo);
        Assert.Equal(fbo, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current,FramebufferTarget.Framebuffer,0);
        Assert.Equal(0, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        GL.DeleteFramebuffer(fbo);

        // Program pipeline
        int pipeline = GL.GenProgramPipeline();
        OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, pipeline);
        Assert.Equal(pipeline, OpenGlStateCache.Current.GetCurrentProgramPipeline());
        OpenGlStateCache.Current.UnbindProgramPipeline();
        Assert.Equal(0, OpenGlStateCache.Current.GetCurrentProgramPipeline());
        GL.DeleteProgramPipeline(pipeline);

        // Renderbuffer
        int rbo = GL.GenRenderbuffer();
        OpenGLImageBindings.BindRenderbuffer(OpenGlStateCache.Current, rbo);
        Assert.Equal(rbo, OpenGlStateCache.Current.GetCurrentRenderbuffer());
        OpenGLImageBindings.UnbindRenderbuffer(OpenGlStateCache.Current);
        Assert.Equal(0, OpenGlStateCache.Current.GetCurrentRenderbuffer());
        GL.DeleteRenderbuffer(rbo);

        // Transform feedback
        int tf = GL.GenTransformFeedback();
        OpenGLFeedbackBindings.Bind(OpenGlStateCache.Current, tf);
        Assert.Equal(tf, OpenGLFeedbackBindings.Get(OpenGlStateCache.Current));
        OpenGLFeedbackBindings.Bind(OpenGlStateCache.Current, 0);
        Assert.Equal(0, OpenGLFeedbackBindings.Get(OpenGlStateCache.Current));
        GL.DeleteTransformFeedback(tf);

        // Texture + sampler
        int tex = GL.GenTexture();
        const int unit = 5;
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, unit, tex);
        Assert.Equal(tex, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, unit));
        OpenGLImageBindings.UnbindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, unit);
        Assert.Equal(0, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, unit));
        GL.DeleteTexture(tex);

        int sampler = GL.GenSampler();
        OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, unit, sampler);
        Assert.Equal(sampler, OpenGlStateCache.Current.GetBoundSampler(unit));
        OpenGLImageBindings.UnbindSampler(OpenGlStateCache.Current, unit);
        Assert.Equal(0, OpenGlStateCache.Current.GetBoundSampler(unit));
        GL.DeleteSampler(sampler);

        // Buffer
        int buffer = GL.GenBuffer();
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer);
        Assert.Equal(buffer, OpenGlStateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
        RHICommandContext.Current.BindBuffer(BufferRole.Vertex, default);
        Assert.Equal(0, OpenGlStateCache.Current.GetBoundBuffer(BufferTarget.ArrayBuffer));
        GL.DeleteBuffer(buffer);
    }

    #endregion

    #region Private

    /// <summary>Owns the native executable and shader modules used by the program unbind scenario.</summary>
    private sealed class SimpleProgram : IDisposable
    {
        private readonly GpuProgramObject owner;

        #region Public API

        /// <summary>Gets the owned native program object.</summary>
        public GpuProgramObject Program => owner;

        private readonly GpuShaderModule vs;
        private readonly GpuShaderModule fs;

        /// <summary>Loads, links, and owns the two-stage program used by the integration test.</summary>
        /// <param name="debugName">Optional native debug label.</param>
        /// <returns>The linked program and its retained shader owners.</returns>
        public static SimpleProgram Create(string? debugName)
        {
            const string vsSource = "tests/GlStateCacheUnbindIntegrationTests_1.vsh";

            const string fsSource = "tests/GlStateCacheUnbindIntegrationTests_2.fsh";

            GpuShaderModule? vs = null;
            GpuShaderModule? fs = null;
            GpuProgramObject? program = null;
            try
            {
                vs = Compile(ShaderType.VertexShader, vsSource);
                fs = Compile(ShaderType.FragmentShader, fsSource);
                program = NativeResourceSetup.CreateProgram(debugName);
                int programId = program.ProgramId;

#if DEBUG
                GlDebug.TryLabel(OpenTK.Graphics.OpenGL4.ObjectLabelIdentifier.Program, programId, debugName);
#endif

                GL.AttachShader(programId, vs.ShaderId);
                GL.AttachShader(programId, fs.ShaderId);
                GL.LinkProgram(programId);

                GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int linkStatus);
                string info = GL.GetProgramInfoLog(programId) ?? string.Empty;
                if (linkStatus == 0)
                {
                    throw new InvalidOperationException($"Program link failed: {info}");
                }

                return new SimpleProgram(program, vs, fs);
            }
            catch
            {
                // Cleanup failures must not replace the construction failure that caused this unwind.
                try { program?.Dispose(); } catch { }
                try { vs?.Dispose(); } catch { }
                try { fs?.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>Unbinds and independently releases the executable and both shader owners.</summary>
        public void Dispose()
        {
            if (owner.ProgramId != 0)
            {
                // Each native operation is best-effort so one driver failure cannot skip owner cleanup.
                try { GL.UseProgram(0); } catch { }
                try { GL.DetachShader(owner.ProgramId, vs.ShaderId); } catch { }
                try { GL.DetachShader(owner.ProgramId, fs.ShaderId); } catch { }
            }

            try { owner.Dispose(); } catch { }
            try { vs.Dispose(); } catch { }
            try { fs.Dispose(); } catch { }
        }

        #endregion

        #region Private

        /// <summary>Creates a linked native program while retaining all owners required for its lifetime.</summary>
        /// <param name="program">The native program owner.</param>
        /// <param name="vs">The vertex shader module.</param>
        /// <param name="fs">The fragment shader module.</param>
        private SimpleProgram(GpuProgramObject program, GpuShaderModule vs, GpuShaderModule fs)
        {
            owner = program;
            this.vs = vs;
            this.fs = fs;
        }

        /// <summary>Loads and validates one compiled shader module.</summary>
        /// <param name="type">The expected shader stage.</param>
        /// <param name="source">The compiled test asset identity.</param>
        /// <returns>The valid module owner.</returns>
        private static GpuShaderModule Compile(ShaderType type, string source)
        {
            var module = BuiltShaderFixture.Load(source, type);
            try
            {
                GL.GetShader(module.ShaderId, ShaderParameter.CompileStatus, out int status);
                string info = GL.GetShaderInfoLog(module.ShaderId) ?? string.Empty;
                if (status == 0)
                {
                    throw new InvalidOperationException($"{type} compile failed: {info}");
                }

                return module;
            }
            catch
            {
                // Release the staged owner after a failed status query while preserving the original failure.
                try { module.Dispose(); } catch { }
                throw;
            }
        }

        #endregion
    }
    #endregion
}
