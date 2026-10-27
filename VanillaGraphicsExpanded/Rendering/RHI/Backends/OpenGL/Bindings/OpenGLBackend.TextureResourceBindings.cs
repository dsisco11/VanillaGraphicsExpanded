using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Batches eligible texture/sampler selections using core multi-bind and the existing per-slot cache.</summary>
internal sealed partial class OpenGLBackend
{
    /// <summary>Retains one resolved slot only while the current complete publication executes.</summary>
    private readonly record struct TexturePublication(int Slot, Legacy.TextureTarget Target, int Texture, int Sampler);
    private readonly List<TexturePublication> texturePublications = new();
    private int[] bindingNames = new int[16];

    #region Private
    #region Selection and policy
    /// <summary>Resolves stable managed identities, then suppresses or batches only the affected declared slots.</summary>
    private void PublishTextureInputs(RHIShader shader, RHIResourceBindings inputs)
    {
        texturePublications.Clear();
        foreach (var active in shader.Interface.PreparedBindings.ActiveEntries)
        {
            var entry = active.Binding;
            if (entry.Contract.Kind != ShaderBindingKind.Sampler) continue;
            inputs.TrySampler(active.Identity, active.Element, out var input);
            var target = OpenGLImageValues.Target((TextureDimension)GpuPreparedBindings.CompatibleTextureTarget(entry.Type));
            int texture = input.Texture.IsEmpty ? 0 : input.Texture.TextureId;
            int sampler = input.Sampler.IsEmpty ? ResolvePolicySampler((ShaderSamplerPolicy)entry.Contract.Binding.Sampler)
                : checked((int)input.Sampler.RequireResource().ResourceId);
            texturePublications.Add(new(entry.Contract.Binding.Slot, target, texture, sampler));
        }
        texturePublications.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));
        if (bindingNames.Length < texturePublications.Count) Array.Resize(ref bindingNames, texturePublications.Count);
        PublishTextureRanges();
        PublishSamplerRanges();
    }
    /// <summary>Resolves existing shared sampler policy only during execution, never during CPU capture.</summary>
    private int ResolvePolicySampler(ShaderSamplerPolicy policy)
    {
        GpuSampler? sampler = policy switch
        {
            ShaderSamplerPolicy.Default => null,
            ShaderSamplerPolicy.NearestClamp => GpuSamplers.NearestClamp,
            ShaderSamplerPolicy.LinearClamp => GpuSamplers.LinearClamp,
            ShaderSamplerPolicy.ShadowCompareLinearClamp => GpuSamplers.ShadowCompareLinearClamp,
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
        if (sampler is null) return 0;
        _ = GpuResourceManager.RequireSamplerReferenceMetadata(sampler);
        RetainExecutionResource(sampler);
        return sampler.SamplerId;
    }
    #endregion
    #region Texture publication
    /// <summary>Uses multi-bind for contiguous nonzero changes, preserving target-only zero clears as a required semantic case.</summary>
    private void PublishTextureRanges()
    {
        for (int index = 0; index < texturePublications.Count;)
        {
            var first = texturePublications[index];
            if (cache.TryGetCachedBoundTexture(first.Target, first.Slot, out int previous) && previous == first.Texture)
            { index++; continue; }
            if (first.Texture == 0)
            {
                // Multi-bind zero clears every target on a unit. Preserve unrelated target bindings and the declared footprint.
                OpenGLImageBindings.BindTexture(cache, first.Target, first.Slot, 0);
                index++;
                continue;
            }
            int count = 0;
            while (index + count < texturePublications.Count)
            {
                var next = texturePublications[index + count];
                if (next.Slot != first.Slot + count || next.Texture == 0
                    || cache.TryGetCachedBoundTexture(next.Target, next.Slot, out int known) && known == next.Texture) break;
                bindingNames[count++] = next.Texture;
            }
            BindTextureRange(index, count);
            index += count;
        }
    }
    /// <summary>Withdraws affected target knowledge and establishes the completed range after one core dispatch.</summary>
    private unsafe void BindTextureRange(int index, int count)
    {
        var first = texturePublications[index];
        for (int item = 0; item < count; item++)
        {
            var input = texturePublications[index + item];
            cache.ForgetImageBinding(input.Target, input.Slot);
        }
        fixed (int* names = bindingNames) GL.BindTextures(first.Slot, count, names);
        RequireContext();
        cache.RecordTextureBind();
        for (int item = 0; item < count; item++)
        {
            var input = texturePublications[index + item];
            cache.ImageBindings(input.Slot)[input.Target] = input.Texture;
        }
    }
    #endregion
    #region Sampler publication
    /// <summary>Batches consecutive changed sampler slots without altering active texture selection.</summary>
    private unsafe void PublishSamplerRanges()
    {
        for (int index = 0; index < texturePublications.Count;)
        {
            var first = texturePublications[index];
            if (cache.SamplerKnowledge(first.Slot) == first.Sampler) { index++; continue; }
            int count = 0;
            while (index + count < texturePublications.Count)
            {
                var next = texturePublications[index + count];
                if (next.Slot != first.Slot + count || cache.SamplerKnowledge(next.Slot) == next.Sampler) break;
                bindingNames[count++] = next.Sampler;
                cache.SetSamplerKnowledge(next.Slot, null);
            }
            fixed (int* names = bindingNames) GL.BindSamplers(first.Slot, count, names);
            RequireContext();
            cache.RecordSamplerBind();
            for (int item = 0; item < count; item++)
            {
                var input = texturePublications[index + item];
                cache.SetSamplerKnowledge(input.Slot, input.Sampler);
            }
            index += count;
        }
    }
    #endregion
    #endregion
}
