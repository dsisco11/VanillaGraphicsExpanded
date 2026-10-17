using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies conservative restart enables and relevant restart/patch payloads.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Preserves dormant restart indices and patch sizes until a participating draw establishes them.</summary>
    private void ApplyPipelineAssembly(PrimitiveAssemblyDesc value, PrimitiveAssemblyStateKnowledge relevant)
    {
        if (relevant.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart)) SetPrimitiveRestartEnabled(value.Restart);
        if (relevant.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex)) SetPrimitiveRestartFixedIndexEnabled(value.FixedIndexRestart);
        if (relevant.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex)) SetRestartIndex(value.RestartIndex);
        if (relevant.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices)) SetPatchVertices(value.PatchVertices);
    }
    #endregion
}
