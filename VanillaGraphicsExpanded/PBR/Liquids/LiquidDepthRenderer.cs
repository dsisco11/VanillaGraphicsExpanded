using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Submits the engine's liquid mesh into its existing depth target using VGE SPIR-V.</summary>
internal sealed class LiquidDepthRenderer : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly LiquidGraphicsSubmission submission;
    private static LiquidDepthRenderer? active;
    private bool failed;
    private bool completed;
    private LiquidWaveFrame waveFrame;

    #region Public API
    /// <summary>Publishes this pass owner at the installed engine depth-draw boundary.</summary>
    internal LiquidDepthRenderer(ICoreClientAPI api)
    {
        this.api = api;
        submission = new(api);
        active = this;
        api.Event.LeaveWorld += LeaveWorld;
    }

    /// <summary>Returns the snapshot only after the VGE depth pass completed this frame.</summary>
    internal static bool TryGetCompletedWaveFrame(out LiquidWaveFrame waves)
    {
        waves = active?.waveFrame ?? default;
        return active?.completed == true;
    }

    /// <summary>Draws borrowed liquid pools and signals whether vanilla's depth draw must be skipped.</summary>
    internal static bool TryRender(ChunkRenderer renderer)
        => active?.Render(renderer) == true;

    /// <summary>Unpublishes this owner without deleting engine framebuffers or meshes.</summary>
    public void Dispose()
    {
        api.Event.LeaveWorld -= LeaveWorld;
        submission.Dispose();
        if (ReferenceEquals(active, this)) active = null;
    }
    #endregion

    #region Private
    /// <summary>Resets a world-specific failure and retires the prior frame's wave snapshot.</summary>
    private void LeaveWorld()
    {
        completed = false;
        failed = false;
    }

    /// <summary>Replaces only the liquid-depth shader and pool submission within the engine pass.</summary>
    private bool Render(ChunkRenderer renderer)
    {
        completed = false;
        if (failed || !LiquidMeshSource.TryGet(api, out var source)
            || !ReferenceEquals(source.Renderer, renderer)
            || !source.TryGetAtlasPools(out var atlases, out var pools)) return false;
        var render = api.Render;
        var buffers = render.FrameBuffers;
        if (buffers.Count <= (int)EnumFrameBuffer.LiquidDepth
            || buffers[(int)EnumFrameBuffer.LiquidDepth] is not { } target
            || target.FboId == 0
            || render.CurrentFrameBuffer?.FboId != target.FboId) return false;
        var program = GpuShaderPrograms.Get<LiquidDepthShaderProgram>(api, "pbr_liquid_depth");
        if (program is null || !GpuUniformRingSystem.TryGetCurrent(out _)) return false;

        bool beganSubmission = false;
        try
        {
            if (!LiquidRenderer.CanTakeOwnership(api, atlases) || !program.EnsureReady()) return false;
            var waves = LiquidWaveFrame.Capture(api);
            program.WaveFrame = waves;
            program.ResetModelTransform();
            if (!submission.Run(program, pools[..atlases.Length], new(submission.Borrow(target), LiquidPipelineStates.DepthOutputs),
                LiquidPipelineStates.Depth, LiquidPipelineStates.DepthBlending, () =>
                {
                    beganSubmission = true;
                    for (int i = 0; i < atlases.Length; i++)
                        pools[i].Render(api.World.Player.Entity.CameraPos, "origin");
                })) return false;
            waveFrame = waves;
            completed = true;
            return true;
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            failed = true;
            api.Logger.Error("[VGE] Liquid depth renderer disabled; vanilla resumes next invocation. {0}", error.ToString());
            // Once a pool has drawn, the engine must not draw the whole depth pass a second time.
            return beganSubmission;
        }
    }
    #endregion
}
