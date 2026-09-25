using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Transfers one original admission and its retained GPU answers without exposing GPU resources to a worker.</summary>
internal readonly record struct WorldProbeCpuFallbackWork(long Id, LumOnWorldProbeTraceWorkItem Item,
    ImmutableArray<uint> Directions, ImmutableArray<WorldProbeTraceAnswerGpu> Answers);

/// <summary>Returns merged answers or explicit failure under the same admission and retained-storage charge.</summary>
internal readonly record struct WorldProbeCpuFallbackResult(WorldProbeCpuFallbackWork Work, bool Success);
