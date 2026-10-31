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
    private ulong[] bindingWriteStamps = new ulong[16];

    #region Private
    #region Selection and policy
    /// <summary>Resolves stable managed identities, then suppresses or batches only the affected declared slots.</summary>
    private void PublishTextureInputs(OpenGLBindingPlan plan)
    {
        texturePublications.Clear();
        foreach (var prepared in plan.Textures)
        {
            var entry = prepared.Selection.Binding;

            var input = prepared.Selection.Sampler;
            var target = OpenGLImageValues.Target((TextureDimension)GpuPreparedBindings.CompatibleTextureTarget(entry.Type));
            int texture = prepared.Texture;
            int sampler = prepared.Sampler;
            texturePublications.Add(new(entry.Contract.Binding.Slot, target, texture, sampler));
        }

        if (bindingNames.Length < texturePublications.Count) Array.Resize(ref bindingNames, texturePublications.Count);
        PublishTextureRanges();
        PublishSamplerRanges();
    }
    /// <summary>Resolves existing shared sampler policy only during execution, never during CPU capture.</summary>
    private int ResolvePolicySampler(ShaderSamplerPolicy policy, RHIResourceBindingStructure structure)
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
        _ = RHIResourceManager.RequireSamplerReferenceMetadata(sampler);
        structure.Observe(sampler);
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
        if (bindingWriteStamps.Length < count) Array.Resize(ref bindingWriteStamps, count);
        for (int item = 0; item < count; item++) bindingWriteStamps[item] = cache.TextureWriteStamp(texturePublications[index + item].Slot);
        fixed (int* names = bindingNames) OpenGLDriverCalls.BindTextures(first.Slot, count, names);
        RequireContext();
        cache.RecordTextureBind();
        bool interrupted = false;
        for (int item = 0; item < count; item++)
        {
            var input = texturePublications[index + item];
            if (bindingWriteStamps[item] != cache.TextureWriteStamp(input.Slot))
            {
                cache.ForgetImageBinding(input.Target, input.Slot);
                interrupted = true;
            }
            else cache.ImageBindings(input.Slot)[input.Target] = input.Texture;
        }
        if (interrupted) throw new InvalidOperationException("Texture publication was interrupted by an overlapping native callback.");
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
            if (bindingWriteStamps.Length < count) Array.Resize(ref bindingWriteStamps, count);
            for (int item = 0; item < count; item++) bindingWriteStamps[item] = cache.SamplerWriteStamp(texturePublications[index + item].Slot);
            fixed (int* names = bindingNames) OpenGLDriverCalls.BindSamplers(first.Slot, count, names);
            RequireContext();
            cache.RecordSamplerBind();
            bool interrupted = false;
            for (int item = 0; item < count; item++)
            {
                var input = texturePublications[index + item];
                if (bindingWriteStamps[item] != cache.SamplerWriteStamp(input.Slot))
                {
                    cache.SetSamplerKnowledge(input.Slot, null);
                    interrupted = true;
                }
                else cache.SetSamplerKnowledge(input.Slot, input.Sampler);
            }
            if (interrupted) throw new InvalidOperationException("Sampler publication was interrupted by an overlapping native callback.");
            index += count;
        }
    }
    #endregion
    #endregion
}
