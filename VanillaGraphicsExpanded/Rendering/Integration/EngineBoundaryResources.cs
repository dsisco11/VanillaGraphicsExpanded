using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Immutable borrowed-slot footprint derived from prepared executable assignments and explicit helper effects.</summary>
internal sealed class EngineBoundaryResources
{
    internal IReadOnlyList<(int Unit, TextureDimension Target)> Textures { get; }
    internal IReadOnlyList<int> Images { get; }
    internal IReadOnlyList<(BufferRangeTarget Target, int Slot)> Buffers { get; }

    internal bool Geometry { get; }
    internal bool Framebuffers { get; }
    internal bool ProgramSelection { get; }

    #region Public API
    /// <summary>Copies explicit helper slots and the selector families the declared work can actually change.</summary>
    internal EngineBoundaryResources(IEnumerable<(int Unit, TextureDimension Target)>? textures = null,
        IEnumerable<int>? images = null, IEnumerable<(BufferRangeTarget Target, int Slot)>? buffers = null,
        bool geometry = true, bool framebuffers = true, bool programSelection = true)
    {
        Geometry = geometry; Framebuffers = framebuffers; ProgramSelection = programSelection;
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
        var textures = new List<(int, TextureDimension)>();
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
                        textures.Add((slot, (TextureDimension)target)); break;
                    case ShaderBindingKind.Image: images.Add(slot); break;
                    case ShaderBindingKind.UniformBlock: buffers.Add((BufferRangeTarget.UniformBuffer, slot)); break;
                    case ShaderBindingKind.StorageBlock: buffers.Add((BufferRangeTarget.ShaderStorageBuffer, slot)); break;
                    case ShaderBindingKind.AtomicCounter: buffers.Add((BufferRangeTarget.AtomicCounterBuffer, slot)); break;
                }
            }
        return new(textures, images, buffers, geometry: false, framebuffers: false);
    }

    /// <summary>Combines participating shader and helper footprints without sharing mutable lists.</summary>
    internal EngineBoundaryResources Union(EngineBoundaryResources other) =>
        new(Textures.Concat(other.Textures), Images.Concat(other.Images), Buffers.Concat(other.Buffers),
            Geometry || other.Geometry, Framebuffers || other.Framebuffers, ProgramSelection || other.ProgramSelection);
    #endregion
}
