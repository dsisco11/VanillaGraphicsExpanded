using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Immutable borrowed-slot footprint derived from prepared executable assignments and explicit helper effects.</summary>
internal sealed class EngineBoundaryResources
{
    internal IReadOnlyList<(int Unit, TextureTarget Target)> Textures { get; }
    internal IReadOnlyList<int> Images { get; }
    internal IReadOnlyList<(BufferRangeTarget Target, int Slot)> Buffers { get; }

    #region Public API
    /// <summary>Copies explicit helper slots; active texture, quad geometry and independent framebuffers are always preserved.</summary>
    internal EngineBoundaryResources(IEnumerable<(int Unit, TextureTarget Target)>? textures = null,
        IEnumerable<int>? images = null, IEnumerable<(BufferRangeTarget Target, int Slot)>? buffers = null)
    {
        Textures = Array.AsReadOnly((textures ?? []).Distinct().ToArray());
        Images = Array.AsReadOnly((images ?? []).Distinct().ToArray());
        Buffers = Array.AsReadOnly((buffers ?? []).Distinct().ToArray());
        foreach (var value in Textures) if (value.Unit < 0) throw new ArgumentOutOfRangeException(nameof(textures));
        foreach (int value in Images) if (value < 0) throw new ArgumentOutOfRangeException(nameof(images));
        foreach (var value in Buffers)
            if (value.Slot < 0 || value.Target is not (BufferRangeTarget.UniformBuffer or BufferRangeTarget.ShaderStorageBuffer or BufferRangeTarget.AtomicCounterBuffer))
                throw new ArgumentOutOfRangeException(nameof(buffers));
    }

    /// <summary>Derives active slot effects without repeating shader-interface reflection.</summary>
    internal static EngineBoundaryResources From(GpuPreparedBindings prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var textures = new List<(int, TextureTarget)>();
        var images = new List<int>();
        var buffers = new List<(BufferRangeTarget, int)>();
        foreach (var entry in prepared.Entries)
            for (int i = 0; i < entry.ActiveElements.Count; i++)
            {
                if (!entry.ActiveElements[i]) continue;
                int slot = entry.Contract.Binding.Slot + i;
                switch (entry.Contract.Kind)
                {
                    case ShaderBindingKind.Sampler:
                        int target = GpuPreparedBindings.CompatibleTextureTarget(entry.Type);
                        if (target == 0) throw new InvalidOperationException("Unsupported prepared sampler footprint.");
                        textures.Add((slot, (TextureTarget)target)); break;
                    case ShaderBindingKind.Image: images.Add(slot); break;
                    case ShaderBindingKind.UniformBlock: buffers.Add((BufferRangeTarget.UniformBuffer, slot)); break;
                    case ShaderBindingKind.StorageBlock: buffers.Add((BufferRangeTarget.ShaderStorageBuffer, slot)); break;
                    case ShaderBindingKind.AtomicCounter: buffers.Add((BufferRangeTarget.AtomicCounterBuffer, slot)); break;
                }
            }
        return new(textures, images, buffers);
    }

    /// <summary>Combines participating shader and helper footprints without sharing mutable lists.</summary>
    internal EngineBoundaryResources Union(EngineBoundaryResources other) =>
        new(Textures.Concat(other.Textures), Images.Concat(other.Images), Buffers.Concat(other.Buffers));
    #endregion
}
