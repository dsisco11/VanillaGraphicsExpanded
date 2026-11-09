using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Integration tests verifying GlStateCache Unbind helpers update cached bindings to zero for each resource type.
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

    /// <summary>Verifies cache and native GL bindings both reach zero after each resource selection is unbound.</summary>
    [Fact]
    public void UnbindMethods_SetBindingsToZero()
    {
        // Arrange: Start from unknown cache knowledge and initialize handles so cleanup can release every raw name on failure.
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        int priorActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        int vao = 0;
        int fbo = 0;
        int pipeline = 0;
        int rbo = 0;
        int tf = 0;
        int tex = 0;
        int sampler = 0;
        int buffer = 0;
        Exception? testFailure = null;
        var cleanupFailures = new List<Exception>();

        // Keep the original test failure while attempting every release; report cleanup failures when the test body succeeds.
        void TryCleanup(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { cleanupFailures.Add(exception); }
        }

        try
        {
            // Program selection crosses the cache adapter into the actual current-program slot.
            using (var program = SimpleProgram.Create("Test.Program"))
            {
                int programId = program.Program.ProgramId;
                OpenGLProgramSelection.Select(cache, programId);
                Assert.Equal(programId, cache.GetCurrentProgram());
                Assert.Equal(programId, GL.GetInteger(GetPName.CurrentProgram));

                OpenGLProgramSelection.Select(cache, 0);
                Assert.Equal(0, cache.GetCurrentProgram());
                Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            }

            // VAO unbinding must update both managed knowledge and the context's vertex-array selector.
            vao = GL.GenVertexArray();
            OpenGLVertexSelection.Bind(cache, vao);
            Assert.Equal(vao, cache.GetCurrentVao());
            Assert.Equal(vao, GL.GetInteger(GetPName.VertexArrayBinding));
            cache.UnbindVertexArray();
            Assert.Equal(0, cache.GetCurrentVao());
            Assert.Equal(0, GL.GetInteger(GetPName.VertexArrayBinding));

            // A combined framebuffer bind updates both draw and read targets.
            fbo = GL.GenFramebuffer();
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, fbo);
            Assert.Equal(fbo, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(fbo, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(fbo, GL.GetInteger(GetPName.ReadFramebufferBinding));
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, 0);
            Assert.Equal(0, cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(0, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(0, GL.GetInteger(GetPName.ReadFramebufferBinding));

            // Program-pipeline selection is a separate native binding from the current program.
            pipeline = GL.GenProgramPipeline();
            OpenGLProgramPipelineSelection.Bind(cache, pipeline);
            Assert.Equal(pipeline, cache.GetCurrentProgramPipeline());
            Assert.Equal(pipeline, GL.GetInteger(GetPName.ProgramPipelineBinding));
            cache.UnbindProgramPipeline();
            Assert.Equal(0, cache.GetCurrentProgramPipeline());
            Assert.Equal(0, GL.GetInteger(GetPName.ProgramPipelineBinding));

            // Renderbuffer selection is verified from the native target after both binding transitions.
            rbo = GL.GenRenderbuffer();
            OpenGLImageBindings.BindRenderbuffer(cache, rbo);
            Assert.Equal(rbo, cache.GetCurrentRenderbuffer());
            Assert.Equal(rbo, GL.GetInteger(GetPName.RenderbufferBinding));
            OpenGLImageBindings.UnbindRenderbuffer(cache);
            Assert.Equal(0, cache.GetCurrentRenderbuffer());
            Assert.Equal(0, GL.GetInteger(GetPName.RenderbufferBinding));

            // Transform feedback has its own context binding even when capture has not begun.
            tf = GL.GenTransformFeedback();
            OpenGLFeedbackBindings.Bind(cache, tf);
            Assert.Equal(tf, OpenGLFeedbackBindings.Get(cache));
            Assert.Equal(tf, GL.GetInteger(GetPName.TransformFeedbackBinding));
            OpenGLFeedbackBindings.Bind(cache, 0);
            Assert.Equal(0, OpenGLFeedbackBindings.Get(cache));
            Assert.Equal(0, GL.GetInteger(GetPName.TransformFeedbackBinding));

            // Texture binding is per active unit, while sampler binding is independently indexed by unit.
            tex = GL.GenTexture();
            const int unit = 5;
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, unit, tex);
            Assert.Equal(tex, cache.GetBoundTexture(TextureTarget.Texture2D, unit));
            Assert.Equal(tex, GL.GetInteger(GetPName.TextureBinding2D));
            OpenGLImageBindings.UnbindTexture(cache, TextureTarget.Texture2D, unit);
            Assert.Equal(0, cache.GetBoundTexture(TextureTarget.Texture2D, unit));
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D));

            sampler = GL.GenSampler();
            OpenGLImageBindings.BindSampler(cache, unit, sampler);
            Assert.Equal(sampler, cache.GetBoundSampler(unit));
            GL.GetInteger((GetIndexedPName)All.SamplerBinding, unit, out int nativeSampler);
            Assert.Equal(sampler, nativeSampler);
            OpenGLImageBindings.UnbindSampler(cache, unit);
            Assert.Equal(0, cache.GetBoundSampler(unit));
            GL.GetInteger((GetIndexedPName)All.SamplerBinding, unit, out nativeSampler);
            Assert.Equal(0, nativeSampler);

            // Vertex-role buffer unbinding must clear the actual array-buffer target.
            buffer = GL.GenBuffer();
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ArrayBuffer, buffer);
            Assert.Equal(buffer, cache.GetBoundBuffer(BufferTarget.ArrayBuffer));
            Assert.Equal(buffer, GL.GetInteger(GetPName.ArrayBufferBinding));
            RHICommandContext.Current.BindBuffer(BufferRole.Vertex, default);
            Assert.Equal(0, cache.GetBoundBuffer(BufferTarget.ArrayBuffer));
            Assert.Equal(0, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            // Direct native teardown also handles assertion failures between bind and unbind; invalidate stale cache entries last.
            TryCleanup(() => GL.UseProgram(0));
            TryCleanup(() => GL.BindVertexArray(0));
            TryCleanup(() => GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0));
            TryCleanup(() => GL.BindProgramPipeline(0));
            TryCleanup(() => GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0));
            TryCleanup(() => GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0));
            TryCleanup(() =>
            {
                GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + 5));
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.BindSampler(5, 0);
            });
            TryCleanup(() => GL.ActiveTexture((TextureUnit)priorActiveTexture));
            TryCleanup(() => GL.BindBuffer(BufferTarget.ArrayBuffer, 0));
            TryCleanup(() => GL.DeleteVertexArray(vao));
            TryCleanup(() => GL.DeleteFramebuffer(fbo));
            TryCleanup(() => GL.DeleteProgramPipeline(pipeline));
            TryCleanup(() => GL.DeleteRenderbuffer(rbo));
            TryCleanup(() => GL.DeleteTransformFeedback(tf));
            TryCleanup(() => GL.DeleteTexture(tex));
            TryCleanup(() => GL.DeleteSampler(sampler));
            TryCleanup(() => GL.DeleteBuffer(buffer));
            TryCleanup(cache.InvalidateAll);

            if (testFailure is null && cleanupFailures.Count > 0)
                throw new AggregateException("Native unbind test cleanup failed.", cleanupFailures);
        }
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
