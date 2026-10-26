using System;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using CoreGL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact engine deletion independently of the managed RHI's separate native loader.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineEarlyDeletionTests(HeadlessGLFixture fixture)
{
    #region Public API
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
