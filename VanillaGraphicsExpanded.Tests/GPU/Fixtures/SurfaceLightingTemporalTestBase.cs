using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Isolates temporal shader retention with fixed anchors; full runtime coverage uses mod-owned callbacks.</summary>
public abstract class SurfaceLightingTemporalTestBase : SurfaceLightingHitTestBase
{
    // Programs owns disposal; histories retain separate textures and all bindings are refreshed per draw.
    private LumOnScreenProbeAtlasTemporalShaderProgram? temporalProgram;

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
        HistoryFrame? result = null;
        Trace(null, resources: history.Resources, worldCache: false, shared: room.Geometry.Scene, surfaceLighting: lighting,
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
        var program = temporalProgram ??= Programs.Create<LumOnScreenProbeAtlasTemporalShaderProgram>(shader => shader.TexelsPerFrame = SurfaceLightingHistoryFixture.DirectionsPerFrame);
        using var use = program.UseScope();
        var anchor = history.Buffers.ProbeAnchorPositionTex!;
        var mask = history.Buffers.ProbeTraceMaskTex!;
        var velocity = history.Buffers.VelocityTex!;
        anchor.UploadDataImmediate(CreateUniformColorData(anchor.Width,anchor.Height,0,0,-3,1));
        mask.UploadDataImmediate(new float[mask.Width*mask.Height*2]);
        velocity.UploadDataImmediate(new float[velocity.Width*velocity.Height*4]);
        var jitter = GetOrCreatePmjJitterTexture(1, 0);
        program.ScreenProbeAtlasCurrent = traced[0];
        program.ScreenProbeAtlasHistory = history.Buffers.ScreenProbeAtlasHistoryTex;
        program.ProbeAnchorPosition = anchor;
        program.ScreenProbeAtlasMetaCurrent = traced[1];
        program.ScreenProbeAtlasMetaHistory = history.Buffers.ScreenProbeAtlasMetaHistoryTex;
        program.VelocityTex = velocity;
        program.PmjJitter = jitter;
        program.ProbeTraceMask = mask;
        UpdateAndBindLumOnFrameUbo(program, frameIndex: history.FrameIndex, anchorJitterEnabled: 0, enableVelocityReprojection: 0);
        program.TemporalAlpha = .9f;
        program.HitDistanceRejectThreshold = .3f;
        TestFramework.RenderQuadTo(program, history.Buffers.ScreenProbeAtlasCurrentFbo!);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
