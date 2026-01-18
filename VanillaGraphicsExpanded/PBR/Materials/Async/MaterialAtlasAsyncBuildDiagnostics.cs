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
    int PendingCpuJobs,
    int CompletedCpuResults,
    int PendingGpuUploads,
    int PendingOverrideUploads,
    double LastTickMs,
    int LastDispatchedCpuJobs,
    int LastGpuUploads,
    int LastOverrideUploads,
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
        bool PageClearDone);

    public DateTime CapturedUtc { get; } = DateTime.UtcNow;
}
