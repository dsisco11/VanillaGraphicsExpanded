using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
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
    internal readonly record struct Entry(GpuBindingEntry Contract, bool Active, ActiveUniformType Type,
        IReadOnlyList<bool> ActiveElements, int MinimumSize = 0);
    /// <summary>Retains an active array element's original authored identity and precomputed slot projection.</summary>
    internal readonly record struct ActiveEntry(ulong Identity, int Element, Entry Binding);

    /// <summary>Provides deterministic contract indices independent of GPU binding slots.</summary>
    internal IReadOnlyList<Entry> Entries { get; }
    /// <summary>Gets active scalar slot projections prepared once, without per-submission record allocation.</summary>
    internal IReadOnlyList<ActiveEntry> ActiveEntries { get; }

    #region Public API
    /// <summary>Validates supplied executable facts and prepares fixed assignments without acquiring native metadata.</summary>
    internal GpuPreparedBindings(GpuBindingContract contract, GpuProgramMetadata metadata)
    {
        if (!metadata.ExecutableAvailable)
            throw new InvalidOperationException("Program executable metadata is unavailable.");
        if (metadata.Resources.IsDefault || metadata.TextureUnits.IsDefault)
            throw new InvalidOperationException("Program executable metadata is incomplete.");
        int textureUnits = GpuSupport.Graphics.MaxCombinedTextureImageUnits;
        int imageUnits = GpuSupport.Graphics.MaxImageUnits;
        var linked = new Dictionary<(ShaderBindingKind Kind, int Slot), (ActiveUniformType Type, int Extent, int BaseSlot)>();
        var uniforms = metadata.Resources.Where(resource => resource.Interface == ProgramInterface.Uniform).ToArray();
        ProgramProperty[] uniformProperties = [ProgramProperty.Location, ProgramProperty.Type, ProgramProperty.ArraySize];
        int[] values = new int[uniformProperties.Length];
        for (int resource = 0; resource < uniforms.Length; resource++)
        {
            ReadProperties(uniforms[resource], uniformProperties, values);
            uniformResourceLocations.Add(uniforms[resource].Index, values[0]);
            var type = (ActiveUniformType)values[1];
            if (values[0] >= 0)
            {
                uniformArrays.Add(values[0], Math.Max(1, values[2]));
            }
            ShaderBindingKind kind;
            if (GpuProgramLayout.IsSamplerType(type)) kind = ShaderBindingKind.Sampler;
            else if (GpuProgramLayout.IsImageType(type)) kind = ShaderBindingKind.Image;
            else
            {
                // Owned executables publish numeric data only through immutable UBO versions.
                // Catch undeclared/manual numeric paths as well as generator-contract mistakes.
                if (values[0] >= 0)
                    throw new InvalidOperationException($"Standalone numeric uniform at location {values[0]} must use a uniform block.");
                continue;
            }
            if (values[0] < 0 || values[2] < 1) throw new InvalidOperationException("Invalid linked texture resource.");
            // Inspect each array element: the compiler owns locations, while the contract owns units.
            int baseSlot = -1;
            for (int element = 0; element < values[2]; element++)
            {
                GpuProgramTextureUnitMetadata? assignment = metadata.TextureUnits
                    .Where(unit => unit.Location == values[0] + element)
                    .Select(unit => (GpuProgramTextureUnitMetadata?)unit).SingleOrDefault();
                if (assignment is null)
                    throw new InvalidOperationException("Program unit metadata is incomplete.");
                int slot = assignment.Value.Unit;
                if (element == 0) baseSlot = slot;
                if (slot < 0 || slot != baseSlot + element || !linked.TryAdd((kind, slot), (type, values[2], baseSlot)))
                    throw new InvalidOperationException($"Ambiguous or invalid linked {kind} unit {slot}.");
                textureLocations.Add((kind, slot), values[0] + element);
            }
        }
        PrepareBlocks(ProgramInterface.UniformBlock, ShaderBindingKind.UniformBlock, linked, metadata);
        PrepareBlocks(ProgramInterface.ShaderStorageBlock, ShaderBindingKind.StorageBlock, linked, metadata);
        foreach (var counter in metadata.Resources.Where(resource => resource.Interface == ProgramInterface.AtomicCounterBuffer))
        {
            int slot = Property(counter, ProgramProperty.BufferBinding);
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
            ActiveUniformType type = default;
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
    internal static int CompatibleTextureTarget(ActiveUniformType type) => type switch
    {
        ActiveUniformType.Sampler1D
            or ActiveUniformType.Sampler1DShadow
            or ActiveUniformType.IntSampler1D
            or ActiveUniformType.UnsignedIntSampler1D
            or ActiveUniformType.Image1D
            or ActiveUniformType.IntImage1D
            or ActiveUniformType.UnsignedIntImage1D => (int)ShaderTextureTarget.Texture1D,
        ActiveUniformType.Sampler2D
            or ActiveUniformType.Sampler2DShadow
            or ActiveUniformType.IntSampler2D
            or ActiveUniformType.UnsignedIntSampler2D
            or ActiveUniformType.Image2D
            or ActiveUniformType.IntImage2D
            or ActiveUniformType.UnsignedIntImage2D => (int)ShaderTextureTarget.Texture2D,
        ActiveUniformType.Sampler3D
            or ActiveUniformType.IntSampler3D
            or ActiveUniformType.UnsignedIntSampler3D
            or ActiveUniformType.Image3D
            or ActiveUniformType.IntImage3D
            or ActiveUniformType.UnsignedIntImage3D => (int)ShaderTextureTarget.Texture3D,
        ActiveUniformType.SamplerCube
            or ActiveUniformType.SamplerCubeShadow
            or ActiveUniformType.IntSamplerCube
            or ActiveUniformType.UnsignedIntSamplerCube
            or ActiveUniformType.ImageCube
            or ActiveUniformType.IntImageCube
            or ActiveUniformType.UnsignedIntImageCube => (int)ShaderTextureTarget.TextureCubeMap,
        ActiveUniformType.Sampler2DArray
            or ActiveUniformType.Sampler2DArrayShadow
            or ActiveUniformType.IntSampler2DArray
            or ActiveUniformType.UnsignedIntSampler2DArray
            or ActiveUniformType.Image2DArray
            or ActiveUniformType.IntImage2DArray
            or ActiveUniformType.UnsignedIntImage2DArray => (int)ShaderTextureTarget.Texture2DArray,
        ActiveUniformType.SamplerBuffer
            or ActiveUniformType.IntSamplerBuffer
            or ActiveUniformType.UnsignedIntSamplerBuffer
            or ActiveUniformType.ImageBuffer
            or ActiveUniformType.IntImageBuffer
            or ActiveUniformType.UnsignedIntImageBuffer => (int)ShaderTextureTarget.TextureBuffer,
        _ => 0
    };
    #endregion

    #region Private
    /// <summary>Matches fixed block slots, including separate stage entries emitted for optimized SPIR-V.</summary>
    private void PrepareBlocks(ProgramInterface resourceInterface, ShaderBindingKind kind,
        Dictionary<(ShaderBindingKind Kind, int Slot), (ActiveUniformType Type, int Extent, int BaseSlot)> linked,
        GpuProgramMetadata metadata)
    {
        var resources = metadata.Resources.Where(resource => resource.Interface == resourceInterface).ToArray();
        ProgramProperty[] properties = [ProgramProperty.BufferBinding, ProgramProperty.BufferDataSize,
            ProgramProperty.ReferencedByVertexShader, ProgramProperty.ReferencedByTessControlShader,
            ProgramProperty.ReferencedByTessEvaluationShader, ProgramProperty.ReferencedByGeometryShader,
            ProgramProperty.ReferencedByFragmentShader, (ProgramProperty)All.ReferencedByComputeShader];
        int[] values = new int[properties.Length];
        var blocks = new Dictionary<int, (int Size, int Stages)>();
        for (int resource = 0; resource < resources.Length; resource++)
        {
            ReadProperties(resources[resource], properties, values);
            int slot = values[0];
            int stages = 0;
            for (int stage = 2; stage < values.Length; stage++)
                if (values[stage] != 0) stages |= 1 << (stage - 2);
            if (blocks.TryGetValue(slot, out var previous))
            {
                // A driver may retain one block per stage after optimization strips names.
                // These entries use the same fixed buffer slot; same-stage collisions,
                // unknown stage ownership and unequal buffer extents remain ambiguous.
                if (previous.Size != values[1] || previous.Stages == 0 || stages == 0
                    || (previous.Stages & stages) != 0)
                    throw new InvalidOperationException($"Ambiguous linked {kind} binding {slot}.");
                blocks[slot] = (previous.Size, previous.Stages | stages);
                continue;
            }
            blocks.Add(slot, (values[1], stages));
            if (!linked.TryAdd((kind, slot), (default, 1, slot)))
                throw new InvalidOperationException($"Ambiguous linked {kind} binding {slot}.");
            // Diagnostic adapters retain one representative; submission binds by slot.
            blockIndices.Add((kind, slot), resources[resource].Index);
            blockSizes.Add((kind, slot), values[1]);
        }
    }

    /// <summary>Requires a complete canonical scalar fact rather than inventing a missing native property.</summary>
    private static int Property(GpuProgramResourceMetadata resource, ProgramProperty property)
    {
        if (resource.Properties is null || !resource.Properties.TryGetValue(property, out var values)
            || values.IsDefault || values.Length != 1)
            throw new InvalidOperationException($"Program metadata omits {property}.");
        return values[0];
    }

    /// <summary>Projects a canonical resource into the existing fixed-slot validation algorithm.</summary>
    private static void ReadProperties(GpuProgramResourceMetadata resource, ProgramProperty[] properties, int[] values)
    {
        for (int index = 0; index < properties.Length; index++) values[index] = Property(resource, properties[index]);
    }

    /// <summary>Compares standardized numeric type identifiers independently of enum spelling.</summary>
    private static bool MatchesType(ActiveUniformType actual, ShaderResourceType expected) => (int)actual == (int)expected;

    #endregion
}
