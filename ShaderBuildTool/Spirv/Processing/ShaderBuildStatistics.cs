namespace ShaderBuildTool.Spirv;

/// <summary>Counts work actually performed; compiler invocations count unique processes rather than aliases.</summary>
internal sealed class ShaderBuildStatistics
{
    internal int RootsReused, RootsExpanded, VariantsReused, VariantsEmitted;
    internal int CompilerReused, CompilerInvocations, InterfacesReused, InterfacesExtracted;
    internal int OutputsRetained, OutputsReplaced, OutputsRepaired, OutputsRemoved;
    internal bool ReceiptReused;
    internal long ExpansionTicks, EmissionTicks, CompilerTicks, InterfaceTicks;

    #region Public API
    /// <summary>Reports selected work separately from verified reuse and concurrent accumulated work time.</summary>
    internal void Report(double inputCheckMs, double selectedProcessingMs, double publicationMs)
    {
        Console.WriteLine($"[SPIR-V] Roots: reused={RootsReused}; expanded={RootsExpanded}; variants: reused={VariantsReused}; emitted={VariantsEmitted}");
        Console.WriteLine($"[SPIR-V] Interfaces: reused={InterfacesReused}; extracted={InterfacesExtracted}; receiptReused={ReceiptReused}");
        Console.WriteLine($"[SPIR-V] Outputs: retained={OutputsRetained}; replaced={OutputsReplaced}; repaired={OutputsRepaired}; removed={OutputsRemoved}");
        Console.WriteLine($"[SPIR-V] Cache hits={CompilerReused}; misses={CompilerInvocations}; shadersRecompiled={CompilerInvocations}; compilerInvocations={CompilerInvocations}");
        Console.WriteLine(FormattableString.Invariant($"[SPIR-V] Timing: inputCheckMs={inputCheckMs:F1}; selectedProcessingMs={selectedProcessingMs:F1}; publicationMs={publicationMs:F1}; emissionWorkMs={Milliseconds(EmissionTicks):F1}; compilerWorkMs={Milliseconds(CompilerTicks):F1}; interfaceWorkMs={Milliseconds(InterfaceTicks):F1}"));
    }
    #endregion

    #region Private
    /// <summary>Converts accumulated stopwatch ticks without conflating parallel work with elapsed time.</summary>
    private static double Milliseconds(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    #endregion
}
