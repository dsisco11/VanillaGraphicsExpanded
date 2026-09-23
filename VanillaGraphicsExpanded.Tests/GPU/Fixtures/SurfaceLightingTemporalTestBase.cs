using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Isolates temporal shader retention with fixed anchors; full runtime coverage uses mod-owned callbacks.</summary>
public abstract class SurfaceLightingTemporalTestBase : SurfaceLightingHitTestBase
{
    /// <summary>Uses the shared material-isolated graphics context.</summary>
    protected SurfaceLightingTemporalTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Exposes raw traces and temporal output for independent component assertions.</summary>
    protected sealed record HistoryFrame(float[] Raw, float[] RawMeta, HitPixels Pixels, bool Reset);

    #region Frame execution
    /// <summary>Traces eight directions per probe and retains the other fifty-six through the real temporal shader.</summary>
    private protected HistoryFrame RenderHistory(SurfaceLightingEnclosureFixture room, SurfaceLightingHistoryFixture history, bool available = true)
    {
        var lighting = available ? room.Snapshot : (VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingSnapshot?)null;
        history.BeginFrame(room, lighting);
        using var placeholder = new NearFieldVoxelFixture();
        HistoryFrame? result = null;
        Trace(placeholder, worldCache: false, shared: room.Geometry.Scene, surfaceLighting: lighting,
            anchorPosition: new(0,0,-3), worldOffset: new(0,32,0), matrixRemainder: new(4,4,7),
            history: history.Buffers.ScreenProbeAtlasHistoryTex, historyMeta: history.Buffers.ScreenProbeAtlasMetaHistoryTex,
            texelsPerFrame: SurfaceLightingHistoryFixture.DirectionsPerFrame, frameIndex: history.FrameIndex, rayMaxDistance: 16,
            consume: traced =>
            {
                Accumulate(traced, history);
                result = new(traced[0].ReadPixels(), traced[1].ReadPixels(),
                    new HitPixels(history.Buffers.ScreenProbeAtlasCurrentTex!.ReadPixels(), history.Buffers.ScreenProbeAtlasMetaCurrentTex!.ReadPixels()), history.ResetThisFrame);
            });
        history.EndFrame();
        return Assert.IsType<HistoryFrame>(result);
    }

    /// <summary>Runs production temporal blending against the previous ping-pong generation without CPU radiance injection.</summary>
    private void Accumulate(GpuFramebuffer traced, SurfaceLightingHistoryFixture history)
    {
        int program = CompileShaderWithDefines("lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh",
            new Dictionary<string,string?> { ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = SurfaceLightingHistoryFixture.DirectionsPerFrame.ToString() });
        var anchor = history.Buffers.ProbeAnchorPositionTex!;
        var mask = history.Buffers.ProbeTraceMaskTex!;
        var velocity = history.Buffers.VelocityTex!;
        anchor.UploadDataImmediate(CreateUniformColorData(anchor.Width,anchor.Height,0,0,-3,1));
        mask.UploadDataImmediate(new float[mask.Width*mask.Height*2]);
        velocity.UploadDataImmediate(new float[velocity.Width*velocity.Height*4]);
        using var jitter = VanillaGraphicsExpanded.LumOn.LumOnPmjJitterTexture.Create(1,0);
        using var parameters = new ObjectParamsUbo("Tests.SurfaceLighting.Temporal");
        try
        {
            Bind("octahedralCurrent",0,traced[0]); Bind("octahedralHistory",1,history.Buffers.ScreenProbeAtlasHistoryTex!);
            Bind("probeAnchorPosition",2,anchor); Bind("probeAtlasMetaCurrent",3,traced[1]);
            Bind("probeAtlasMetaHistory",4,history.Buffers.ScreenProbeAtlasMetaHistoryTex!);
            Bind("velocityTex",5,velocity); Bind("pmjJitter",6,jitter); Bind("probeTraceMask",7,mask);
            UpdateAndBindLumOnFrameUbo(program,frameIndex:history.FrameIndex,anchorJitterEnabled:0,enableVelocityReprojection:0);
            UniformBlockBindingUtil.EnsureBlockBound(program,LumOnProbeParamsUbo.BlockName,GpuBindingRegistry.Ubo.Object);
            parameters.UploadAndBind(new LumOnProbeParamsUbo { TemporalAlpha=.9f, HitDistanceRejectThreshold=.3f }.Bytes);
            TestFramework.RenderQuadTo(program,history.Buffers.ScreenProbeAtlasCurrentFbo!);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { TestShaderInterfaces.DeleteProgram(program); }

        /// <summary>Binds a production upstream attachment to its declared sampler.</summary>
        void Bind(string name,int unit,GpuTexture texture)
        {
            GL.UseProgram(program); GL.Uniform1(TestShaderInterfaces.GetUniformLocation(program,name),unit);
            texture.Bind(unit); GL.UseProgram(0);
        }
    }
    #endregion
}
