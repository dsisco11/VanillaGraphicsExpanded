using System;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using CoreGL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact engine deletion independently of the managed RHI's separate native loader.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineEarlyDeletionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Exact query and capture deletion use their original loader, including reserved zero-name calls.</summary>
    [Theory]
    [InlineData(GpuResourceKind.Query)] [InlineData(GpuResourceKind.TransformFeedback)]
    public void AuxiliaryDeletionUsesOriginalNativeLoader(GpuResourceKind kind)
    {
        fixture.EnsureContextValid();
        var harmony = new Harmony("VGE.Tests.EarlyAuxiliaryDeletion");
        string method = kind == GpuResourceKind.Query ? nameof(CoreGL.IsQuery) : nameof(CoreGL.IsTransformFeedback);
        harmony.Patch(AccessTools.Method(typeof(CoreGL), method, [typeof(int)]),
            prefix: new HarmonyMethod(typeof(EngineEarlyDeletionTests), nameof(RejectCoreProbe)));
        int id = 0;
        try
        {
            if (kind == GpuResourceKind.Query)
            {
                id = GL.GenQuery(); GL.BeginQuery(QueryTarget.SamplesPassed, id); GL.EndQuery(QueryTarget.SamplesPassed);
                OpenGLAuxiliaryDeletionCalls.Core.DeleteQuery(id);
                OpenGLAuxiliaryDeletionCalls.Arb.DeleteQueries(2, new[] { id, 0 });
                Assert.False(GL.IsQuery(id));
            }
            else
            {
                id = GL.GenTransformFeedback(); GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, id);
                OpenGLAuxiliaryDeletionCalls.Core.DeleteTransformFeedbacks(3, new[] { id, id, 0 });
                Assert.False(GL.IsTransformFeedback(id));
            }
            Assert.NotEqual(0, id);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            if (kind == GpuResourceKind.Query) GL.DeleteQuery(id); else GL.DeleteTransformFeedback(id);
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Exact sync deletion preserves its full pointer width and original native loader.</summary>
    [Fact]
    public void SyncDeletionUsesOriginalNativeLoader()
    {
        fixture.EnsureContextValid();
        var harmony = new Harmony("VGE.Tests.EarlySyncDeletion");
        harmony.Patch(AccessTools.Method(typeof(CoreGL), nameof(CoreGL.IsSync), [typeof(IntPtr)]),
            prefix: new HarmonyMethod(typeof(EngineEarlyDeletionTests), nameof(RejectCoreProbe)));
        nint id = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        try
        {
            OpenGLAuxiliaryDeletionCalls.Core.DeleteSync(id);
            Assert.False(GL.IsSync(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Original-loader outcomes reconcile deletion without consulting unadmitted GL4 entry points.</summary>
    [Theory]
    [InlineData(GpuResourceKind.Texture)] [InlineData(GpuResourceKind.Renderbuffer)]
    [InlineData(GpuResourceKind.Sampler)] [InlineData(GpuResourceKind.Buffer)]
    public void ExactDeletionUsesItsOriginalNativeLoader(GpuResourceKind kind)
    {
        fixture.EnsureContextValid();
        var harmony = new Harmony("VGE.Tests.EarlyDeletion");
        foreach (string name in new[] { nameof(CoreGL.IsTexture), nameof(CoreGL.IsRenderbuffer), nameof(CoreGL.IsSampler), nameof(CoreGL.IsBuffer) })
            harmony.Patch(AccessTools.Method(typeof(CoreGL), name, [typeof(int)]), prefix: new HarmonyMethod(typeof(EngineEarlyDeletionTests), nameof(RejectCoreProbe)));
        int id = 0;
        try
        {
            switch (kind)
            {
                case GpuResourceKind.Texture:
                    id = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, id);
                    EngineStateCalls.DeleteTexture(id); Assert.False(GL.IsTexture(id)); break;
                case GpuResourceKind.Renderbuffer:
                    id = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, id);
                    EngineStateCalls.DeleteRenderbuffer(id); Assert.False(GL.IsRenderbuffer(id)); break;
                case GpuResourceKind.Sampler:
                    id = GL.GenSampler(); EngineStateCalls.DeleteSampler(id); Assert.False(GL.IsSampler(id)); break;
                case GpuResourceKind.Buffer:
                    id = GL.GenBuffer(); GL.BindBuffer(BufferTarget.ArrayBuffer, id);
                    EngineStateCalls.DeleteBuffer(id); Assert.False(GL.IsBuffer(id)); break;
            }
            Assert.NotEqual(0, id);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            switch (kind)
            {
                case GpuResourceKind.Texture: GL.DeleteTexture(id); break;
                case GpuResourceKind.Renderbuffer: GL.DeleteRenderbuffer(id); break;
                case GpuResourceKind.Sampler: GL.DeleteSampler(id); break;
                case GpuResourceKind.Buffer: GL.DeleteBuffer(id); break;
            }
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Rejects accidental use of a separate loader without risking a null native function pointer.</summary>
    private static void RejectCoreProbe() => throw new InvalidOperationException("Exact legacy deletion must not use the managed core loader.");
    #endregion
}
