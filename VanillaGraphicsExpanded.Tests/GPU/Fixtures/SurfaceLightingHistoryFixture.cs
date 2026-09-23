using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Retains production probe buffers and dependency state across controlled lighting frames.</summary>
internal sealed class SurfaceLightingHistoryFixture : IDisposable
{
    private readonly BinaryShaderApiFixture assets = new();
    private readonly ProbeLightingHistoryDependencies dependencies = new();
    public ShaderLightingResources Resources { get; }
    public LumOnBufferManager Buffers { get; }
    public int FrameIndex { get; private set; }
    public bool ResetThisFrame { get; private set; }
    public const int DirectionsPerFrame = 8;
    public const int SweepFrames = 64 / DirectionsPerFrame;

    #region Retained state
    /// <summary>Allocates the same atlas formats and ping-pong ownership used by the renderer.</summary>
    public SurfaceLightingHistoryFixture()
    {
        Resources = new(assets.Api);
        Buffers = Resources.EnsureScreen(4, 4, 2);
        Buffers.ClearHistory();
    }

    /// <summary>Applies the production dependency decision before tracing can copy retained directions.</summary>
    public void BeginFrame(SurfaceLightingEnclosureFixture room, SurfaceLightingSnapshot? lighting)
    {
        ResetThisFrame = dependencies.Synchronize(room.Geometry.Scene, lighting);
        if (ResetThisFrame) Buffers.ClearHistory();
    }

    /// <summary>Publishes the completed temporal atlas as next frame's history.</summary>
    public void EndFrame()
    {
        Buffers.SwapRadianceBuffers();
        FrameIndex++;
    }

    /// <summary>Disposes retained GPU resources before their API boundary.</summary>
    public void Dispose() { Resources.Dispose(); assets.Dispose(); }
    #endregion
}

