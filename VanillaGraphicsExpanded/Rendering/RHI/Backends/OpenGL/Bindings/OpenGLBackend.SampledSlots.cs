using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes explicitly declared sampled slots through the same validation, resource guards and cache as shader tables.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void BindSampledTextures(ReadOnlySpan<RHISampledTextureSlot> inputs)
    {
        RequireContext();
        if (executionResources.Count != 0) throw new InvalidOperationException("Resource execution cannot recurse.");
        try
        {
            // Resolve and guard the complete set before touching selectors; unavailable input never partially publishes.
            texturePublications.Clear();
            foreach (var input in inputs)
            {
                if ((uint)input.Slot >= (uint)Capabilities.MaxCombinedTextureImageUnits)
                    throw new ArgumentOutOfRangeException(nameof(inputs));
                foreach (var prior in texturePublications)
                    if (prior.Slot == input.Slot) throw new ArgumentException("Duplicate sampled texture slot.", nameof(inputs));
                var target = OpenGLImageValues.Target(input.Dimension);
                if (!input.Texture.IsEmpty)
                {
                    var metadata = input.Texture.RequireMetadata();
                    if (metadata.Target != target) throw new ArgumentException("Sampled texture dimension does not match its declared slot.", nameof(inputs));
                    RenderPass.ValidateSampledTexture(input.Texture);
                    input.Texture.Resource!.RequireOriginalContext();
                    RetainExecutionResource(input.Texture.Resource);
                }
                int sampler = 0;
                if (!input.Sampler.IsEmpty)
                {
                    var owner = input.Sampler.RequireResource();
                    _ = GpuResourceManager.RequireSamplerReferenceMetadata(owner);
                    RetainExecutionResource(owner);
                    sampler = checked((int)owner.ResourceId);
                }
                texturePublications.Add(new(input.Slot, target, input.Texture.IsEmpty ? 0 : input.Texture.TextureId, sampler));
            }
            texturePublications.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));
            if (bindingNames.Length < texturePublications.Count) Array.Resize(ref bindingNames, texturePublications.Count);
            PublishTextureRanges();
            PublishSamplerRanges();
            RequireContext();
        }
        finally { ReleaseExecutionResources(); }
    }
    #endregion
}
