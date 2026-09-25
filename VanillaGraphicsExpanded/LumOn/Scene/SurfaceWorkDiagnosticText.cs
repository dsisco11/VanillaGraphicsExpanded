using System;
using System.Text;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Formats bounded aggregate measurements only at the existing periodic reporting cadence.</summary>
internal static class SurfaceWorkDiagnosticText
{
    #region Formatting
    /// <summary>Labels sample denominators and emits nonzero counters in their explicit ray/texel categories.</summary>
    public static string Format(SurfaceWorkStage stage, SurfaceWorkMeasurement sample)
    {
        var text = new StringBuilder($"{stage}[dispatch:{sample.Submitted} sampled:{sample.Collected} skip:{sample.Skipped} readFail:{sample.ReadFailures} pages:{sample.Pages} " +
            $"submitMs:{sample.SubmitMilliseconds:0.###} gpuMs:{sample.GpuMilliseconds:0.###} observedMs:{sample.CompletionMilliseconds:0.###}");
        for (int i = 0; i < sample.Counters.Length; i++)
            if (sample.Counters[i] != 0) text.Append($" {(SurfaceWorkCounter)i}:{sample.Counters[i]}");
        return text.Append(']').ToString();
    }

    /// <summary>Reports pending age and observed page-completion latency without counting eviction as success.</summary>
    public static string FormatQueue(string name, SurfacePageProgress progress, long now) =>
        $"{name}[pending:{progress.PendingCount} oldestMs:{progress.OldestMilliseconds(now)} " +
        $"completed:{progress.Completed} completionMs:{progress.CompletionMilliseconds}]";
    #endregion
}
