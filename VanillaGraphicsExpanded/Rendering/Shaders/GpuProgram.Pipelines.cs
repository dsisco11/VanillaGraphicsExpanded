using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Coordinates renderer-owned contracts with graphics executable candidate publication.</summary>
public abstract partial class GpuProgram
{
    private readonly HashSet<GraphicsPipelineAsset> rendererPipelines = new();
    private ulong pipelineContractRevision;

    #region Internal API
    /// <summary>Registers a live renderer contract without acquiring disposal authority over its realization.</summary>
    internal void RegisterPipeline(GraphicsPipelineAsset asset)
    {
        RequireOutsideSubmission();
        lock (settingsLock) { rendererPipelines.Add(asset); pipelineContractRevision++; }
    }
    /// <summary>Withdraws a retired renderer contract so reload never recreates unused pipelines.</summary>
    internal void UnregisterPipeline(GraphicsPipelineAsset asset)
    {
        lock (settingsLock) { if (rendererPipelines.Remove(asset)) pipelineContractRevision++; }
    }
    #endregion

    #region Private
    /// <summary>Prepares every registered renderer contract, retaining installed realizations if any candidate fails.</summary>
    private GraphicsPipelineCandidates PreparePipelineCandidates(RHIShader shader)
    {
        GraphicsPipelineAsset[] contracts;
        ulong revision;
        lock (settingsLock) { contracts = rendererPipelines.ToArray(); revision = pipelineContractRevision; }
        var result = new GraphicsPipelineCandidates(revision);
        try
        {
            foreach (var contract in contracts) result.Add(contract, contract.PrepareReplacement(shader));
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    /// <summary>Owns only unpublished pipeline candidates until a coherent executable installation succeeds.</summary>
    private sealed class GraphicsPipelineCandidates(ulong revision) : IDisposable
    {
        private readonly List<(GraphicsPipelineAsset Asset, RHIGraphicsPipeline Pipeline)> candidates = new();
        private bool installed;
        /// <summary>Identifies the renderer contract set captured before candidate construction.</summary>
        internal ulong Revision => revision;
        /// <summary>Retains each newly created candidate for cleanup or publication.</summary>
        internal void Add(GraphicsPipelineAsset asset, RHIGraphicsPipeline pipeline) => candidates.Add((asset, pipeline));
        /// <summary>Swaps prevalidated realizations with no callbacks or native operations in the publication step.</summary>
        internal RHIGraphicsPipeline[] Install()
        {
            // Allocate retirement bookkeeping before the first visible assignment.
            var previous = new RHIGraphicsPipeline[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) previous[i] = candidates[i].Asset.Install(candidates[i].Pipeline);
            installed = true;
            return previous;
        }
        /// <summary>Retires only abandoned candidates; installed realizations belong to their original renderers.</summary>
        public void Dispose()
        {
            if (!installed) foreach (var candidate in candidates) candidate.Pipeline.Dispose();
        }
    }
    #endregion
}
