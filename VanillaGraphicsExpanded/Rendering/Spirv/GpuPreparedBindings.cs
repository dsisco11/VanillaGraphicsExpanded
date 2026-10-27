using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Owns immutable resource activity and numeric assignments for one validated executable.</summary>
internal sealed class GpuPreparedBindings
{
    private readonly Dictionary<int, int> uniformArrays = new();
    private readonly Dictionary<ulong, Entry> entriesByIdentity = new();
    private readonly Dictionary<int, int> uniformResourceLocations = new();
    private readonly Dictionary<(ShaderBindingKind Kind, int Slot), int> blockIndices = new();
    private readonly Dictionary<(ShaderBindingKind Kind, int Slot), int> blockSizes = new();
    private readonly Dictionary<(ShaderBindingKind Kind, int Slot), int> textureLocations = new();
    /// <summary>Describes one contract entry without exposing reflection uniform locations.</summary>
    internal readonly record struct Entry(GpuBindingEntry Contract, bool Active, ShaderValueType Type,
        IReadOnlyList<bool> ActiveElements, int MinimumSize = 0);
    /// <summary>Retains an active array element's original authored identity and precomputed slot projection.</summary>
    internal readonly record struct ActiveEntry(ulong Identity, int Element, Entry Binding);

    /// <summary>Provides deterministic contract indices independent of GPU binding slots.</summary>
    internal IReadOnlyList<Entry> Entries { get; }
    /// <summary>Gets active scalar slot projections prepared once, without per-submission record allocation.</summary>
    internal IReadOnlyList<ActiveEntry> ActiveEntries { get; }

    #region Public API
    /// <summary>Validates supplied executable facts and prepares fixed assignments without acquiring native metadata.</summary>
    internal GpuPreparedBindings(GpuBindingContract contract, GpuProgramMetadata metadata, int textureUnits, int imageUnits)
    {
        if (!metadata.ExecutableAvailable)
            throw new InvalidOperationException("Program executable metadata is unavailable.");
        if (metadata.Resources.IsDefault || metadata.TextureUnits.IsDefault)
            throw new InvalidOperationException("Program executable metadata is incomplete.");
        var linked = new Dictionary<(ShaderBindingKind Kind, int Slot), (ShaderValueType Type, int Extent, int BaseSlot)>();
        var uniforms = metadata.Resources.Where(resource => resource.Interface == ProgramResourceKind.Uniform).ToArray();
        for (int resource = 0; resource < uniforms.Length; resource++)
        {
            var uniform = uniforms[resource];
            int location = uniform.Location ?? throw new InvalidOperationException("Uniform location is unavailable.");
            int extent = uniform.ArraySize ?? throw new InvalidOperationException("Uniform extent is unavailable.");
            var type = uniform.Type ?? throw new InvalidOperationException("Uniform type is unavailable.");
            uniformResourceLocations.Add(uniform.Index, location);
            if (location >= 0) uniformArrays.Add(location, Math.Max(1, extent));
            ShaderBindingKind kind;
            if (GpuProgramLayout.IsSamplerType(type)) kind = ShaderBindingKind.Sampler;
            else if (GpuProgramLayout.IsImageType(type)) kind = ShaderBindingKind.Image;
            else
            {
                // Owned executables publish numeric data only through immutable UBO versions.
                // Catch undeclared/manual numeric paths as well as generator-contract mistakes.
                if (location >= 0)
                    throw new InvalidOperationException($"Standalone numeric uniform at location {location} must use a uniform block.");
                continue;
            }
            if (location < 0 || extent < 1) throw new InvalidOperationException("Invalid linked texture resource.");
            // Inspect each array element: the compiler owns locations, while the contract owns units.
            int baseSlot = -1;
            for (int element = 0; element < extent; element++)
            {
                GpuProgramTextureUnitMetadata? assignment = metadata.TextureUnits
                    .Where(unit => unit.Location == location + element)
                    .Select(unit => (GpuProgramTextureUnitMetadata?)unit).SingleOrDefault();
                if (assignment is null)
                    throw new InvalidOperationException("Program unit metadata is incomplete.");
                int slot = assignment.Value.Unit;
                if (element == 0) baseSlot = slot;
                if (slot < 0 || slot != baseSlot + element || !linked.TryAdd((kind, slot), (type, extent, baseSlot)))
                    throw new InvalidOperationException($"Ambiguous or invalid linked {kind} unit {slot}.");
                textureLocations.Add((kind, slot), location + element);
            }
        }
        PrepareBlocks(ProgramResourceKind.UniformBlock, ShaderBindingKind.UniformBlock, linked, metadata);
        PrepareBlocks(ProgramResourceKind.ShaderStorageBlock, ShaderBindingKind.StorageBlock, linked, metadata);
        foreach (var counter in metadata.Resources.Where(resource => resource.Interface == ProgramResourceKind.AtomicCounterBuffer))
        {
            int slot = counter.BufferBinding ?? throw new InvalidOperationException("Counter binding is unavailable.");
            if (!linked.TryAdd((ShaderBindingKind.AtomicCounter, slot), (default, 1, slot)))
                throw new InvalidOperationException($"Ambiguous linked atomic counter binding {slot}.");
        }
        var entries = new List<Entry>();
        var matched = new HashSet<(ShaderBindingKind Kind, int Slot)>();
        foreach (var entry in contract.Entries)
        {
            int limit = entry.Kind switch
            {
                ShaderBindingKind.Sampler => textureUnits,
                ShaderBindingKind.Image => imageUnits,
                _ => int.MaxValue
            };
            if (entry.Binding.Slot > limit - entry.Binding.ArrayLength)
                throw new InvalidOperationException($"Contract {entry.Kind} range exceeds available units: '{entry.Name}'.");
            bool active = false;
            ShaderValueType type = default;
            var elements = new bool[entry.Binding.ArrayLength];
            for (int element = 0; element < entry.Binding.ArrayLength; element++)
            {
                if (!linked.TryGetValue((entry.Kind, entry.Binding.Slot + element), out var resource)) continue;
                active = true;
                elements[element] = true;
                matched.Add((entry.Kind, entry.Binding.Slot + element));
                type = resource.Type;
                if (resource.BaseSlot != entry.Binding.Slot || resource.Extent > entry.Binding.ArrayLength)
                    throw new InvalidOperationException($"Linked array exceeds contract extent: '{entry.Name}'.");
                if (entry.Binding.ShaderType is { } expected && expected != ShaderResourceType.Unspecified && !MatchesType(type, expected))
                    throw new InvalidOperationException($"Incompatible linked type for '{entry.Name}': {type}, expected {expected}.");
                if (entry.Binding.TextureTarget != 0 && CompatibleTextureTarget(type) != entry.Binding.TextureTarget)
                    throw new InvalidOperationException($"Incompatible linked texture target for '{entry.Name}': {type}.");
            }
            entries.Add(new(entry, active, type, Array.AsReadOnly(elements), blockSizes.GetValueOrDefault((entry.Kind, entry.Binding.Slot))));
        }
        foreach (var resource in linked.Keys)
            if (!matched.Contains(resource))
                throw new InvalidOperationException($"Linked {resource.Kind} slot {resource.Slot} has no binding contract entry.");
        Entries = Array.AsReadOnly(entries.ToArray());
        var activeEntries = new List<ActiveEntry>();
        foreach (var entry in entries)
        {
            if (!entriesByIdentity.TryAdd(GpuBindingEntry.Identity(entry.Contract.Kind, entry.Contract.Name), entry))
                throw new InvalidOperationException("Conflicting generated resource identities.");
            for (int element = 0; element < entry.ActiveElements.Count; element++)
            {
                if (!entry.ActiveElements[element]) continue;
                var projected = entry with
                {
                    Active = true,
                    Contract = entry.Contract with { Binding = entry.Contract.Binding with
                        { Slot = entry.Contract.Binding.Slot + element, ArrayLength = 1 } }
                };
                activeEntries.Add(new(GpuBindingEntry.Identity(entry.Contract.Kind, entry.Contract.Name), element, projected));
            }
        }
        ActiveEntries = Array.AsReadOnly(activeEntries.ToArray());
    }

    /// <summary>Selects prepared activity by generated numeric identity; excluded variant inputs remain inactive.</summary>
    internal Entry Resolve(ulong identity) => entriesByIdentity.GetValueOrDefault(identity);

    /// <summary>Checks retained sampler/image location metadata without exposing addresses in resource entries.</summary>
    internal bool ContainsUniformLocation(int location)
    {
        foreach (var array in uniformArrays)
            if (location >= array.Key && location - array.Key < array.Value) return true;
        return false;
    }

    /// <summary>Returns sampler/image array extent for existing engine interface projections.</summary>
    internal int UniformArrayLength(int location) => uniformArrays.GetValueOrDefault(location);

    /// <summary>Reads the retained inspection location for the existing diagnostic resource adapter.</summary>
    internal int UniformResourceLocation(int index) => uniformResourceLocations.GetValueOrDefault(index, -1);

    /// <summary>Projects private inspection addresses for engine APIs without changing fixed-slot submission.</summary>
    internal int TextureUniformLocation(ShaderBindingKind kind, int slot) => textureLocations.GetValueOrDefault((kind, slot), -1);

    /// <summary>Returns a block's private reflected index for existing diagnostic adapters.</summary>
    internal int BlockResourceIndex(ShaderBindingKind kind, int slot) => blockIndices.GetValueOrDefault((kind, slot), -1);

    /// <summary>Converts a linked sampler or image dimension into the contract's texture target.</summary>
    internal static int CompatibleTextureTarget(ShaderValueType type) => type switch
    {
        ShaderValueType.Sampler1D
            or ShaderValueType.Sampler1DShadow
            or ShaderValueType.IntSampler1D
            or ShaderValueType.UnsignedIntSampler1D
            or ShaderValueType.Image1D
            or ShaderValueType.IntImage1D
            or ShaderValueType.UnsignedIntImage1D => (int)ShaderTextureTarget.Texture1D,
        ShaderValueType.Sampler2D
            or ShaderValueType.Sampler2DShadow
            or ShaderValueType.IntSampler2D
            or ShaderValueType.UnsignedIntSampler2D
            or ShaderValueType.Image2D
            or ShaderValueType.IntImage2D
            or ShaderValueType.UnsignedIntImage2D => (int)ShaderTextureTarget.Texture2D,
        ShaderValueType.Sampler3D
            or ShaderValueType.IntSampler3D
            or ShaderValueType.UnsignedIntSampler3D
            or ShaderValueType.Image3D
            or ShaderValueType.IntImage3D
            or ShaderValueType.UnsignedIntImage3D => (int)ShaderTextureTarget.Texture3D,
        ShaderValueType.SamplerCube
            or ShaderValueType.SamplerCubeShadow
            or ShaderValueType.IntSamplerCube
            or ShaderValueType.UnsignedIntSamplerCube
            or ShaderValueType.ImageCube
            or ShaderValueType.IntImageCube
            or ShaderValueType.UnsignedIntImageCube => (int)ShaderTextureTarget.TextureCubeMap,
        ShaderValueType.Sampler2DArray
            or ShaderValueType.Sampler2DArrayShadow
            or ShaderValueType.IntSampler2DArray
            or ShaderValueType.UnsignedIntSampler2DArray
            or ShaderValueType.Image2DArray
            or ShaderValueType.IntImage2DArray
            or ShaderValueType.UnsignedIntImage2DArray => (int)ShaderTextureTarget.Texture2DArray,
        ShaderValueType.SamplerBuffer
            or ShaderValueType.IntSamplerBuffer
            or ShaderValueType.UnsignedIntSamplerBuffer
            or ShaderValueType.ImageBuffer
            or ShaderValueType.IntImageBuffer
            or ShaderValueType.UnsignedIntImageBuffer => (int)ShaderTextureTarget.TextureBuffer,
        _ => 0
    };
    #endregion

    #region Private
    /// <summary>Matches fixed block slots, including separate stage entries emitted for optimized SPIR-V.</summary>
    private void PrepareBlocks(ProgramResourceKind resourceInterface, ShaderBindingKind kind,
        Dictionary<(ShaderBindingKind Kind, int Slot), (ShaderValueType Type, int Extent, int BaseSlot)> linked,
        GpuProgramMetadata metadata)
    {
        var resources = metadata.Resources.Where(resource => resource.Interface == resourceInterface).ToArray();
        var blocks = new Dictionary<int, (int Size, int Stages)>();
        for (int resource = 0; resource < resources.Length; resource++)
        {
            var block = resources[resource];
            int slot = block.BufferBinding ?? throw new InvalidOperationException("Block binding is unavailable.");
            int size = block.BufferDataSize ?? throw new InvalidOperationException("Block extent is unavailable.");
            int stages = (int)(block.ReferencedStages ?? throw new InvalidOperationException("Block stage ownership is unavailable."));
            if (blocks.TryGetValue(slot, out var previous))
            {
                // A driver may retain one block per stage after optimization strips names.
                // These entries use the same fixed buffer slot; same-stage collisions,
                // unknown stage ownership and unequal buffer extents remain ambiguous.
                if (previous.Size != size || previous.Stages == 0 || stages == 0
                    || (previous.Stages & stages) != 0)
                    throw new InvalidOperationException($"Ambiguous linked {kind} binding {slot}.");
                blocks[slot] = (previous.Size, previous.Stages | stages);
                continue;
            }
            blocks.Add(slot, (size, stages));
            if (!linked.TryAdd((kind, slot), (default, 1, slot)))
                throw new InvalidOperationException($"Ambiguous linked {kind} binding {slot}.");
            // Diagnostic adapters retain one representative; submission binds by slot.
            blockIndices.Add((kind, slot), resources[resource].Index);
            blockSizes.Add((kind, slot), size);
        }
    }

    /// <summary>Matches authored resource requirements against semantic executable types.</summary>
    private static bool MatchesType(ShaderValueType actual, ShaderResourceType expected) => actual == (expected switch
    {
        ShaderResourceType.Sampler1D => ShaderValueType.Sampler1D,
        ShaderResourceType.Sampler2D => ShaderValueType.Sampler2D,
        ShaderResourceType.Sampler3D => ShaderValueType.Sampler3D,
        ShaderResourceType.SamplerCube => ShaderValueType.SamplerCube,
        ShaderResourceType.Sampler1DShadow => ShaderValueType.Sampler1DShadow,
        ShaderResourceType.Sampler2DShadow => ShaderValueType.Sampler2DShadow,
        ShaderResourceType.Sampler2DRect => ShaderValueType.Sampler2DRect,
        ShaderResourceType.Sampler2DRectShadow => ShaderValueType.Sampler2DRectShadow,
        ShaderResourceType.Sampler1DArray => ShaderValueType.Sampler1DArray,
        ShaderResourceType.Sampler2DArray => ShaderValueType.Sampler2DArray,
        ShaderResourceType.SamplerBuffer => ShaderValueType.SamplerBuffer,
        ShaderResourceType.Sampler1DArrayShadow => ShaderValueType.Sampler1DArrayShadow,
        ShaderResourceType.Sampler2DArrayShadow => ShaderValueType.Sampler2DArrayShadow,
        ShaderResourceType.SamplerCubeShadow => ShaderValueType.SamplerCubeShadow,
        ShaderResourceType.IntSampler1D => ShaderValueType.IntSampler1D,
        ShaderResourceType.IntSampler2D => ShaderValueType.IntSampler2D,
        ShaderResourceType.IntSampler3D => ShaderValueType.IntSampler3D,
        ShaderResourceType.IntSamplerCube => ShaderValueType.IntSamplerCube,
        ShaderResourceType.IntSampler2DRect => ShaderValueType.IntSampler2DRect,
        ShaderResourceType.IntSampler1DArray => ShaderValueType.IntSampler1DArray,
        ShaderResourceType.IntSampler2DArray => ShaderValueType.IntSampler2DArray,
        ShaderResourceType.IntSamplerBuffer => ShaderValueType.IntSamplerBuffer,
        ShaderResourceType.UnsignedIntSampler1D => ShaderValueType.UnsignedIntSampler1D,
        ShaderResourceType.UnsignedIntSampler2D => ShaderValueType.UnsignedIntSampler2D,
        ShaderResourceType.UnsignedIntSampler3D => ShaderValueType.UnsignedIntSampler3D,
        ShaderResourceType.UnsignedIntSamplerCube => ShaderValueType.UnsignedIntSamplerCube,
        ShaderResourceType.UnsignedIntSampler2DRect => ShaderValueType.UnsignedIntSampler2DRect,
        ShaderResourceType.UnsignedIntSampler1DArray => ShaderValueType.UnsignedIntSampler1DArray,
        ShaderResourceType.UnsignedIntSampler2DArray => ShaderValueType.UnsignedIntSampler2DArray,
        ShaderResourceType.UnsignedIntSamplerBuffer => ShaderValueType.UnsignedIntSamplerBuffer,
        ShaderResourceType.SamplerCubeMapArray => ShaderValueType.SamplerCubeMapArray,
        ShaderResourceType.SamplerCubeMapArrayShadow => ShaderValueType.SamplerCubeMapArrayShadow,
        ShaderResourceType.IntSamplerCubeMapArray => ShaderValueType.IntSamplerCubeMapArray,
        ShaderResourceType.UnsignedIntSamplerCubeMapArray => ShaderValueType.UnsignedIntSamplerCubeMapArray,
        ShaderResourceType.Image1D => ShaderValueType.Image1D,
        ShaderResourceType.Image2D => ShaderValueType.Image2D,
        ShaderResourceType.Image3D => ShaderValueType.Image3D,
        ShaderResourceType.Image2DRect => ShaderValueType.Image2DRect,
        ShaderResourceType.ImageCube => ShaderValueType.ImageCube,
        ShaderResourceType.ImageBuffer => ShaderValueType.ImageBuffer,
        ShaderResourceType.Image1DArray => ShaderValueType.Image1DArray,
        ShaderResourceType.Image2DArray => ShaderValueType.Image2DArray,
        ShaderResourceType.ImageCubeMapArray => ShaderValueType.ImageCubeMapArray,
        ShaderResourceType.Image2DMultisample => ShaderValueType.Image2DMultisample,
        ShaderResourceType.Image2DMultisampleArray => ShaderValueType.Image2DMultisampleArray,
        ShaderResourceType.IntImage1D => ShaderValueType.IntImage1D,
        ShaderResourceType.IntImage2D => ShaderValueType.IntImage2D,
        ShaderResourceType.IntImage3D => ShaderValueType.IntImage3D,
        ShaderResourceType.IntImage2DRect => ShaderValueType.IntImage2DRect,
        ShaderResourceType.IntImageCube => ShaderValueType.IntImageCube,
        ShaderResourceType.IntImageBuffer => ShaderValueType.IntImageBuffer,
        ShaderResourceType.IntImage1DArray => ShaderValueType.IntImage1DArray,
        ShaderResourceType.IntImage2DArray => ShaderValueType.IntImage2DArray,
        ShaderResourceType.IntImageCubeMapArray => ShaderValueType.IntImageCubeMapArray,
        ShaderResourceType.IntImage2DMultisample => ShaderValueType.IntImage2DMultisample,
        ShaderResourceType.IntImage2DMultisampleArray => ShaderValueType.IntImage2DMultisampleArray,
        ShaderResourceType.UnsignedIntImage1D => ShaderValueType.UnsignedIntImage1D,
        ShaderResourceType.UnsignedIntImage2D => ShaderValueType.UnsignedIntImage2D,
        ShaderResourceType.UnsignedIntImage3D => ShaderValueType.UnsignedIntImage3D,
        ShaderResourceType.UnsignedIntImage2DRect => ShaderValueType.UnsignedIntImage2DRect,
        ShaderResourceType.UnsignedIntImageCube => ShaderValueType.UnsignedIntImageCube,
        ShaderResourceType.UnsignedIntImageBuffer => ShaderValueType.UnsignedIntImageBuffer,
        ShaderResourceType.UnsignedIntImage1DArray => ShaderValueType.UnsignedIntImage1DArray,
        ShaderResourceType.UnsignedIntImage2DArray => ShaderValueType.UnsignedIntImage2DArray,
        ShaderResourceType.UnsignedIntImageCubeMapArray => ShaderValueType.UnsignedIntImageCubeMapArray,
        ShaderResourceType.UnsignedIntImage2DMultisample => ShaderValueType.UnsignedIntImage2DMultisample,
        ShaderResourceType.UnsignedIntImage2DMultisampleArray => ShaderValueType.UnsignedIntImage2DMultisampleArray,
        ShaderResourceType.Sampler2DMultisample => ShaderValueType.Sampler2DMultisample,
        ShaderResourceType.IntSampler2DMultisample => ShaderValueType.IntSampler2DMultisample,
        ShaderResourceType.UnsignedIntSampler2DMultisample => ShaderValueType.UnsignedIntSampler2DMultisample,
        ShaderResourceType.Sampler2DMultisampleArray => ShaderValueType.Sampler2DMultisampleArray,
        ShaderResourceType.IntSampler2DMultisampleArray => ShaderValueType.IntSampler2DMultisampleArray,
        ShaderResourceType.UnsignedIntSampler2DMultisampleArray => ShaderValueType.UnsignedIntSampler2DMultisampleArray,
        _ => ShaderValueType.Unknown
    });

    #endregion
}
