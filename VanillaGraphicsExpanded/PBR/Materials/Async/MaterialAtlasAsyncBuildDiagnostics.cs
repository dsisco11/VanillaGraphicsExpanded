using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct MaterialAtlasAsyncBuildDiagnostics(
    int GenerationId,
    bool IsCancelled,
    bool IsComplete,
    int TotalTiles,
    int CompletedTiles,
    int TotalOverrides,
    int OverridesApplied,
    int TotalNormalDepthJobs,
    int CompletedNormalDepthJobs,
    int PendingCpuJobs,
    int CompletedCpuResults,
    int PendingGpuUploads,
    int PendingOverrideUploads,
    int PendingNormalDepthJobs,
    double LastTickMs,
    int LastDispatchedCpuJobs,
    int LastGpuUploads,
    int LastOverrideUploads,
    int LastNormalDepthUploads,
    IReadOnlyList<MaterialAtlasAsyncBuildDiagnostics.Page> Pages)
{
    internal readonly record struct Page(
        int AtlasTextureId,
        int Width,
        int Height,
        int PendingTiles,
        int InFlightTiles,
        int CompletedTiles,
        int PendingOverrides,
        int CompletedOverrides,
        bool PageClearDone,
        int NormalDepthPendingJobs,
        int NormalDepthCompletedJobs,
        bool NormalDepthPageClearDone);

    public DateTime CapturedUtc { get; } = DateTime.UtcNow;
}
