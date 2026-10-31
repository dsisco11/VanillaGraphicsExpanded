using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes retained explicit sampled slots using the same executed cache as shader resource tables.</summary>
internal sealed partial class OpenGLBackend
{
    /// <summary>Holds only native realization of one context-owned semantic declaration.</summary>
    private sealed class SampledTexturePlan
    {
        internal ulong Revision = ulong.MaxValue;
        internal TexturePublication[] Slots = [];
    }
    private readonly ConditionalWeakTable<RHISampledTextureBindings, SampledTexturePlan> sampledPlans = new();

    #region Public API
    /// <inheritdoc />
    public void BindSampledTextures(RHISampledTextureBindings inputs)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(inputs);
        inputs.Prepare(Capabilities, this);
        var plan = sampledPlans.GetValue(inputs, static _ => new());
        if (plan.Revision != inputs.Revision)
        {
            var slots = new TexturePublication[inputs.Inputs.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                var input = inputs.Inputs[index];
                slots[index] = new(input.Slot, OpenGLImageValues.Target(input.Dimension),
                    input.Texture.Resource is { } texture ? checked((int)texture.ResourceId) : 0,
                    input.Sampler.IsEmpty ? 0 : checked((int)input.Sampler.RequireResource().ResourceId));
            }
            plan.Slots = slots;
            plan.Revision = inputs.Revision;
        }
        // Pass overlap depends on the current pass, independently of stable image compatibility.
        foreach (var input in inputs.Inputs)
            if (!input.Texture.IsEmpty) RenderPass.ValidateSampledTexture(input.Texture);
        texturePublications.Clear();
        texturePublications.AddRange(plan.Slots);
        if (bindingNames.Length < plan.Slots.Length) Array.Resize(ref bindingNames, plan.Slots.Length);
        try { PublishTextureRanges(); PublishSamplerRanges(); RequireContext(); }
        finally { texturePublications.Clear(); }
    }
    #endregion
}
