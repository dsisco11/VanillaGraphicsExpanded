using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Owns immutable resource activity and numeric assignments for one validated executable.</summary>
internal sealed class GpuPreparedBindings
{
    private readonly Dictionary<int, int> uniformArrays = new();
    private readonly Dictionary<int, ActiveUniformType> uniformTypes = new();
    private readonly Dictionary<ulong, Entry> entriesByIdentity = new();
    private readonly Dictionary<int, int> uniformResourceLocations = new();
    private readonly Dictionary<(ShaderBindingKind Kind, int Slot), int> blockIndices = new();
    private readonly Dictionary<(ShaderBindingKind Kind, int Slot), int> textureLocations = new();
    /// <summary>Describes one contract entry without exposing reflection uniform locations.</summary>
    internal readonly record struct Entry(GpuBindingEntry Contract, bool Active, ActiveUniformType Type,
        IReadOnlyList<bool> ActiveElements);

    /// <summary>Provides deterministic contract indices independent of GPU binding slots.</summary>
    internal IReadOnlyList<Entry> Entries { get; }
    /// <summary>Counts linked-interface queries during preparation, excluding context limit queries.</summary>
    internal int ReflectionQueries { get; private set; }

    #region Public API
    /// <summary>Validates fixed resource assignments once before the candidate executable is published.</summary>
    internal GpuPreparedBindings(int program, GpuBindingContract contract)
    {
        GL.GetInteger(GetPName.MaxCombinedTextureImageUnits, out int textureUnits);
        GL.GetInteger((GetPName)All.MaxImageUnits, out int imageUnits);
        var linked = new Dictionary<(ShaderBindingKind Kind, int Slot), (ActiveUniformType Type, int Extent, int BaseSlot)>();
        GL.GetProgramInterface(program, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
        ReflectionQueries++;
        ProgramProperty[] uniformProperties = [ProgramProperty.Location, ProgramProperty.Type, ProgramProperty.ArraySize];
        int[] values = new int[uniformProperties.Length];
        for (int resource = 0; resource < count; resource++)
        {
            GL.GetProgramResource(program, ProgramInterface.Uniform, resource, uniformProperties.Length,
                uniformProperties, values.Length, out _, values);
            ReflectionQueries++;
            uniformResourceLocations.Add(resource, values[0]);
            var type = (ActiveUniformType)values[1];
            if (values[0] >= 0)
            {
                uniformArrays.Add(values[0], Math.Max(1, values[2]));
                uniformTypes.Add(values[0], type);
            }
            ShaderBindingKind kind;
            if (GpuProgramLayout.IsSamplerType(type)) kind = ShaderBindingKind.Sampler;
            else if (GpuProgramLayout.IsImageType(type)) kind = ShaderBindingKind.Image;
            else continue;
            if (values[0] < 0 || values[2] < 1) throw new InvalidOperationException("Invalid linked texture resource.");
            // Inspect each array element: the compiler owns locations, while the contract owns units.
            int baseSlot = -1;
            for (int element = 0; element < values[2]; element++)
            {
                GL.GetUniform(program, values[0] + element, out int slot);
                ReflectionQueries++;
                if (element == 0) baseSlot = slot;
                if (slot < 0 || slot != baseSlot + element || !linked.TryAdd((kind, slot), (type, values[2], baseSlot)))
                    throw new InvalidOperationException($"Ambiguous or invalid linked {kind} unit {slot}.");
                textureLocations.Add((kind, slot), values[0] + element);
            }
        }
        ReadBlocks(program, ProgramInterface.UniformBlock, ShaderBindingKind.UniformBlock, linked);
        ReadBlocks(program, ProgramInterface.ShaderStorageBlock, ShaderBindingKind.StorageBlock, linked);
        GL.GetProgram(program, GetProgramParameterName.ActiveAtomicCounterBuffers, out int counters);
        ReflectionQueries++;
        for (int counter = 0; counter < counters; counter++)
        {
            GL.GetActiveAtomicCounterBuffer(program, counter, AtomicCounterBufferParameter.AtomicCounterBufferBinding, out int slot);
            ReflectionQueries++;
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
            entries.Add(new(entry, active, type, Array.AsReadOnly(elements)));
        }
        foreach (var resource in linked.Keys)
            if (!matched.Contains(resource))
                throw new InvalidOperationException($"Linked {resource.Kind} slot {resource.Slot} has no binding contract entry.");
        Entries = Array.AsReadOnly(entries.ToArray());
        foreach (var entry in entries)
            if (!entriesByIdentity.TryAdd(GpuBindingEntry.Identity(entry.Contract.Kind, entry.Contract.Name), entry))
                throw new InvalidOperationException("Conflicting generated resource identities.");
    }

    /// <summary>Selects prepared activity by generated numeric identity; excluded variant inputs remain inactive.</summary>
    internal Entry Resolve(ulong identity) => entriesByIdentity.GetValueOrDefault(identity);

    /// <summary>Checks legacy numeric uniform metadata without exposing reflection locations in resource entries.</summary>
    internal bool ContainsUniformLocation(int location)
    {
        foreach (var array in uniformArrays)
            if (location >= array.Key && location - array.Key < array.Value) return true;
        return false;
    }

    /// <summary>Returns ordinary uniform extent for existing element-aware value upload compatibility.</summary>
    internal int UniformArrayLength(int location) => uniformArrays.GetValueOrDefault(location);

    /// <summary>Returns retained ordinary-value type metadata without another driver query.</summary>
    internal ActiveUniformType UniformType(int location) => uniformTypes.GetValueOrDefault(location);

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
    /// <summary>Matches block resources by their fixed namespace and rejects duplicate assignments.</summary>
    private void ReadBlocks(int program, ProgramInterface resourceInterface, ShaderBindingKind kind,
        Dictionary<(ShaderBindingKind Kind, int Slot), (ActiveUniformType Type, int Extent, int BaseSlot)> linked)
    {
        GL.GetProgramInterface(program, resourceInterface, ProgramInterfaceParameter.ActiveResources, out int count);
        ReflectionQueries++;
        ProgramProperty[] properties = [ProgramProperty.BufferBinding];
        int[] slot = new int[1];
        for (int resource = 0; resource < count; resource++)
        {
            GL.GetProgramResource(program, resourceInterface, resource, properties.Length, properties, slot.Length, out _, slot);
            ReflectionQueries++;
            if (!linked.TryAdd((kind, slot[0]), (default, 1, slot[0])))
                throw new InvalidOperationException($"Ambiguous linked {kind} binding {slot[0]}.");
            blockIndices.Add((kind, slot[0]), resource);
        }
    }

    /// <summary>Compares standardized numeric type identifiers independently of enum spelling.</summary>
    private static bool MatchesType(ActiveUniformType actual, ShaderResourceType expected) => (int)actual == (int)expected;

    #endregion
}
