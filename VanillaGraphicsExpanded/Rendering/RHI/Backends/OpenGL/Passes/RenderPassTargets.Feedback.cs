using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Retains feedback results for actual resource selections and pipeline write state on this target generation.</summary>
internal sealed partial class RenderPassTargets
{
    private readonly ConditionalWeakTable<GpuResource, Feedback> feedback = new();

    #region Internal API
    /// <summary>Checks alias/subresource relationships initially or after the sampled owner's relevant change.</summary>
    internal void PrepareFeedback(GpuTextureRef texture, int? level, int? layer, DepthStencilDesc? state)
    {
        var result = feedback.GetValue(texture.Resource!, static resource => new(resource));
        var key = (level, layer, state);
        if (!result.Results.TryGetValue(key, out var failure))
        {
            try
            {
                var metadata = texture.RequireMetadata();
                if (level is { } selected) RenderPass.ValidateTextureRange(texture, metadata, selected, layer, state);
                else foreach (var image in metadata.Levels)
                    if (image.Allocated && image.Level >= metadata.BaseLevel && image.Level <= metadata.MaxLevel)
                        RenderPass.ValidateTextureRange(texture, metadata, image.Level, null, state);
            }
            catch (Exception error) { failure = error; }
            result.Results.Add(key, failure);
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    #endregion

    #region Private
    /// <summary>Withdraws only results using this texture; target changes replace the enclosing prepared target facts.</summary>
    private sealed class Feedback
    {
        internal readonly Dictionary<(int? Level, int? Layer, DepthStencilDesc? State), Exception?> Results = new();
        /// <summary>Connects one weak prepared consumer to the original texture owner.</summary>
        internal Feedback(GpuResource resource) { resource.ObservePreparation(this, Invalidate); }
        /// <summary>Discards compatibility only, without queries, synchronization or resource ownership.</summary>
        private void Invalidate() { Results.Clear(); }
    }
    #endregion
}
