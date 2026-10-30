namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Separates required wait-boundary validation from optional failure diagnostics.</summary>
internal sealed partial class SurfaceLightingConsumerRuntimeFixture
{
    #region Wait observations
    /// <summary>Retains validated energies from this boundary for reuse in a failure message.</summary>
    private readonly record struct WaitOutputEnergies(float Final, float World, float Trace, float Filter, float Gather);

    /// <summary>Preserves full nonempty/finite validation previously hidden in eager assertion-message interpolation.</summary>
    private WaitOutputEnergies ObserveRequiredWaitOutputs() => new(
        Energy(FinalPixels(), "final indirect output"),
        Energy(WorldPixels(), "world radiance atlas"),
        Energy(Screen.ScreenProbeAtlasHistoryTex!.ReadPixels(), "screen trace history"),
        Energy(Screen.ScreenProbeAtlasFilteredTex!.ReadPixels(), "screen filtered history"),
        Energy(Screen.IndirectHalfTex!.ReadPixels(), "half-resolution gather"));

    /// <summary>Collects optional GPU diagnostics only after settling fails, reusing required output observations.</summary>
    private string DescribeWaitFailure(int maximumFrames, WaitOutputEnergies observations)
    {
        // Confidence and anchors carried no correctness assertions in the old message. Keep their
        // readbacks failure-only; the five validated outputs above remain mandatory on success.
        return $"Runtime failed to settle in {maximumFrames} frames; frames={Cache.Frames}, workerReads={World.WorkerReads}, final={observations.Final}, world={observations.World}, worldConfidence={WorldConfidence}, trace={observations.Trace}, filter={observations.Filter}, gather={observations.Gather}, anchors={string.Join(",",LayeredTestTexture.Read(Screen.ProbeAnchors!, 0))}, pending={HasPendingSurfaceLightingQueries}, programs={string.Join(',',LoadedPrograms)}, logs={string.Join('|',Cache.Logs.TakeLast(8))}";
    }
    #endregion
}
