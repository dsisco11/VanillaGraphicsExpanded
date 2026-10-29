using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native deletion outcomes independently of managed wrapper selection and saved-original dispatch.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineEarlyDeletionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Query and capture deletion check actual outcomes while retaining zero/duplicate-name semantics.</summary>
    [Theory]
    [InlineData(GpuResourceKind.Query)] [InlineData(GpuResourceKind.TransformFeedback)]
    public void AuxiliaryDeletionUsesNativeOutcome(GpuResourceKind kind)
    {
        fixture.EnsureContextValid();
        using var capture = new DeletionOutcomeCapture(kind == GpuResourceKind.Query ? "glIsQuery" : "glIsTransformFeedback");
        int id = 0;
        try
        {
            if (kind == GpuResourceKind.Query)
            {
                id = GL.GenQuery(); GL.BeginQuery(QueryTarget.SamplesPassed, id); GL.EndQuery(QueryTarget.SamplesPassed);
                capture.Reset(); GL.DeleteQuery(id);
                Assert.True(capture.Reads > 0);
                GL.Arb.DeleteQueries(2, new[] { id, 0 });
                Assert.False(GL.IsQuery(id));
            }
            else
            {
                id = GL.GenTransformFeedback(); GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, id);
                capture.Reset(); GL.DeleteTransformFeedbacks(3, new[] { id, id, 0 });
                Assert.True(capture.Reads > 0);
                Assert.False(GL.IsTransformFeedback(id));
            }
            Assert.NotEqual(0, id);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (kind == GpuResourceKind.Query) GL.DeleteQuery(id); else GL.DeleteTransformFeedback(id);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Sync deletion retains native pointer width and reads the actual deletion outcome.</summary>
    [Fact]
    public void SyncDeletionUsesNativeOutcome()
    {
        fixture.EnsureContextValid();
        using var capture = new DeletionOutcomeCapture("glIsSync", sync: true);
        nint id = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        try
        {
            capture.Reset(); GL.DeleteSync(id);
            Assert.True(capture.Reads > 0);
            Assert.False(GL.IsSync(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Each image/buffer kind retires its original name using its narrow native outcome.</summary>
    [Theory]
    [InlineData(GpuResourceKind.Texture)] [InlineData(GpuResourceKind.Renderbuffer)]
    [InlineData(GpuResourceKind.Sampler)] [InlineData(GpuResourceKind.Buffer)]
    public void ExactDeletionUsesNativeOutcome(GpuResourceKind kind)
    {
        fixture.EnsureContextValid();
        using var capture = new DeletionOutcomeCapture("glIs" + kind);
        int id = 0;
        try
        {
            switch (kind)
            {
                case GpuResourceKind.Texture:
                    id = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, id);
                    capture.Reset(); GL.DeleteTexture(id); break;
                case GpuResourceKind.Renderbuffer:
                    id = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, id);
                    capture.Reset(); GL.DeleteRenderbuffer(id); break;
                case GpuResourceKind.Sampler:
                    id = GL.GenSampler(); capture.Reset(); GL.DeleteSampler(id); break;
                case GpuResourceKind.Buffer:
                    id = GL.GenBuffer(); GL.BindBuffer(BufferTarget.ArrayBuffer, id);
                    capture.Reset(); GL.DeleteBuffer(id); break;
            }
            Assert.NotEqual(0, id);
            Assert.True(capture.Reads > 0);
            Assert.False(capture.Exists);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            switch (kind)
            {
                case GpuResourceKind.Texture: GL.DeleteTexture(id); break;
                case GpuResourceKind.Renderbuffer: GL.DeleteRenderbuffer(id); break;
                case GpuResourceKind.Sampler: GL.DeleteSampler(id); break;
                case GpuResourceKind.Buffer: GL.DeleteBuffer(id); break;
            }
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Decorates the native kind-specific outcome without substituting results or errors.</summary>
    private sealed unsafe class DeletionOutcomeCapture : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        /// <summary>Matches numeric object identity and its GLboolean result.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte NamedOutcome(uint id);
        /// <summary>Matches pointer-width sync identity and its GLboolean result.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte SyncOutcome(nint id);

        #region Internal API
        internal int Reads { get; private set; }
        /// <summary>Retains the last real GLboolean outcome for the observed kind.</summary>
        internal bool Exists { get; private set; }
        /// <summary>Decorates one exact native ABI for both binding roots.</summary>
        internal DeletionOutcomeCapture(string symbol, bool sync = false)
        {
            nint original = native.Original(symbol);
            if (sync) native.Add(symbol, new SyncOutcome(id =>
            {
                byte result = ((delegate* unmanaged[Stdcall]<nint, byte>)original)(id);
                Reads++; Exists = result != 0; return result;
            }));
            else native.Add(symbol, new NamedOutcome(id =>
            {
                byte result = ((delegate* unmanaged[Stdcall]<uint, byte>)original)(id);
                Reads++; Exists = result != 0; return result;
            }));
            native.Install();
        }
        /// <summary>Excludes setup from the next outcome interval.</summary>
        internal void Reset() => Reads = 0;
        #endregion
        #region Public API
        /// <summary>Restores ordinary loading after observing native deletion.</summary>
        public void Dispose() => native.Dispose();
        #endregion
    }
    #endregion
}
