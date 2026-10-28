using OpenTK.Graphics.OpenGL;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using CoreGL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies external capture activity using real native acceptance and per-object executed history.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OpenTKFeedbackObservationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Successful transitions and proven rejection preserve activity without repeated discovery.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void ExternalActivityPreservesAcceptedAndRejectedTransitions(bool core)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var feedback = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
            using var other = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription {
                Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 64,
                Usage = BufferUsage.StreamRead }).RequireResource();
            int shader = GL.CreateShader(ShaderType.VertexShader), program = GL.CreateProgram();
            try
            {
                GL.ShaderSource(shader, "#version 450\nout float captured; void main(){ captured=1.0; gl_Position=vec4(0,0,0,1); }");
                GL.CompileShader(shader);
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
                Assert.Equal(1, compiled);
                GL.AttachShader(program, shader);
                GL.TransformFeedbackVaryings(program, 1, ["captured"], TransformFeedbackMode.InterleavedAttribs);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                Assert.True(linked == 1, GL.GetProgramInfoLog(program));
                GL.UseProgram(program);
                GL.TransformFeedbackBufferBase(feedback.TransformFeedbackId, 0, buffer.BufferId);
                Bind(core, feedback.TransformFeedbackId);
                using var native = new NativeDriverCapture();
                int reads = 0;
                nint original = native.Original("glGetBooleanv");
                native.Add("glGetBooleanv", new BooleanQuery((parameter, output) =>
                {
                    native.Observe(() =>
                    {
                        if (parameter == (int)All.TransformFeedbackActive || parameter == (int)All.TransformFeedbackPaused) reads++;
                    });
                    ((delegate* unmanaged[Stdcall]<int, byte*, void>)original)(parameter, output);
                }));
                native.Install();
                var cache = OpenGlStateCache.Current;
                Activity(core, 0);
                AssertActivity(cache, true, false);
                Assert.Equal(2, reads);
                Bind(core, other.TransformFeedbackId);
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                Assert.Equal(feedback.TransformFeedbackId, OpenGLFeedbackBindings.GetExternalSelection(cache));
                AssertActivity(cache, true, false);
                Assert.Equal(2, reads);
                Activity(core, 1);
                AssertActivity(cache, true, true);
                Assert.Equal(2, reads);
                Activity(core, 1);
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                AssertActivity(cache, true, true);
                Assert.Equal(2, reads);
                Activity(core, 2);
                AssertActivity(cache, true, false);
                Assert.Equal(4, reads);
                Activity(core, 2);
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                AssertActivity(cache, true, false);
                Assert.Equal(4, reads);
                Activity(core, 3);
                AssertActivity(cache, false, false);
                Assert.Equal(4, reads);
                Activity(core, 3);
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                AssertActivity(cache, false, false);
                Assert.Equal(4, reads);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally
            {
                if (GL.GetBoolean((GetPName)All.TransformFeedbackActive)) GL.EndTransformFeedback();
                GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0);
                GL.UseProgram(0);
                GL.DeleteProgram(program);
                GL.DeleteShader(shader);
            }
        }
        finally { fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Checks the existing object's observed activity without reading native state.</summary>
    private static void AssertActivity(OpenGlStateCache cache, bool active, bool paused)
    {
        Assert.True(cache.TryGetFeedbackActivity(out var state));
        Assert.Equal((active, paused), state);
    }

    /// <summary>Exercises native activity entrypoints in either supported binding root.</summary>
    private static void Activity(bool core, int operation)
    {
        if (core)
        {
            switch (operation)
            {
                case 0: CoreGL.BeginTransformFeedback(OpenTK.Graphics.OpenGL4.TransformFeedbackPrimitiveType.Points); break;
                case 1: CoreGL.PauseTransformFeedback(); break;
                case 2: CoreGL.ResumeTransformFeedback(); break;
                default: CoreGL.EndTransformFeedback(); break;
            }
        }
        else switch (operation)
        {
            case 0: GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points); break;
            case 1: GL.PauseTransformFeedback(); break;
            case 2: GL.ResumeTransformFeedback(); break;
            default: GL.EndTransformFeedback(); break;
        }
    }

    /// <summary>Exercises object selection in the same binding root as the activity transitions.</summary>
    private static void Bind(bool core, int id)
    {
        if (core) CoreGL.BindTransformFeedback(OpenTK.Graphics.OpenGL4.TransformFeedbackTarget.TransformFeedback, id);
        else GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, id);
    }

    /// <summary>Matches native boolean output queries used by the activity oracle.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private unsafe delegate void BooleanQuery(int parameter, byte* output);
    #endregion
}
