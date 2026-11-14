using System;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Exposes the selected controlled-integration owner to existing debug views without GPU readback.</summary>
internal static class ShadowDiagnostics
{
    internal static Func<ShadowFrameDiagnostics?>? Snapshot { get; set; }
    internal static Func<GpuShaderStorageBuffer?>? Readiness { get; set; }
    internal static int LightCapacity { get; set; }
    internal static Func<GpuShaderStorageBuffer?>? CandidateHeaders { get; set; }
    internal static Func<GpuShaderStorageBuffer?>? PageEntries { get; set; }
    internal static Func<GpuShaderStorageBuffer?>? SampleFailures { get; set; }
    internal static ImmutableArray<ShadowCandidateDiagnostic> Candidates { get; set; }
    /// <summary>Formats CPU-known conservative costs separately from GPU current/stale/coverage records.</summary>
    internal static string Text
    {
        get
        {
            var s=Snapshot?.Invoke();
            if(s is null)return "No active shadow frame owner. Surface lights remain illuminated without virtual shadow attenuation.";
            return $"Frame {s.Frame}; demand {s.RequestedPages}; admitted replacements {s.WritablePages}\n"+
                $"Tracked bytes {s.ResidentBytes}; draining {s.DrainingBytes}; rejected reservations {s.BudgetRejections}; last ceiling {s.BudgetRejectionCause}\n"+
                $"Conservative slots {s.Used.Slots}/{s.Limit.Slots}; views {s.Used.Instances}; draws {s.Used.Draws}\n"+
                $"Raster pixels {s.Used.RasterPixels}; raster/atomic proxy {s.Used.RasterSamples}\n"+
                $"Traversal cells {s.Used.Cells}; slabs {s.Used.Slabs}; probes {s.Used.Probes}\n"+
                $"Last completed GPU interval: {(s.LastGpuMilliseconds is { } ms?$"{ms:F3} ms (frame {s.LastGpuFrame})":"pending")}; missing timing samples {s.MissingTimingSamples}; missing priority samples {s.MissingPrioritySamples}\n"+
                string.Join("; ",Candidates.IsDefault?Enumerable.Empty<string>():Candidates.Select(candidate=>$"{candidate.Source.Kind}:{candidate.Source.Owner}/{candidate.Source.Slot} candidate {candidate.AgeFrames} frames; motion {candidate.NormalizedMotion:F2} of covered interval; refresh {candidate.NeedsRefresh}"))+"\n"+
                string.Join("; ",s.Decisions.Select(decision=>$"{decision.Source.Kind}:{decision.Source.Owner}/{decision.Source.Slot} detail {decision.Detail}; {decision.Reason}; waiting {decision.WaitingSeconds:F2}s; refresh attempt age {decision.SecondsSinceRefreshAttempt:F2}s"))+"\n"+
                $"Whole-light GPU rows include readiness, detail, demand misses and unexpected surface sampling misses.\n{s.Failure}";
        }
    }
}
